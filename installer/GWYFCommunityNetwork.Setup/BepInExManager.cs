using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace GwyfCommunity.Setup;

/// <summary>
/// Probes an existing BepInEx install and installs/upgrades BepInEx 6 IL2CPP
/// by overlaying the pinned bleeding-edge zip into the game root. Extraction is
/// non-destructive to BepInEx/config, BepInEx/plugins and BepInEx/patchers.
/// </summary>
internal static class BepInExManager
{
    public enum InstallState
    {
        /// <summary>No BepInEx 6 IL2CPP core present; fresh install needed.</summary>
        Absent,
        /// <summary>IL2CPP core present with a supported build number.</summary>
        Good,
        /// <summary>IL2CPP core present but too old (or build number unknown) to trust with game build 303.</summary>
        UpgradeNeeded,
    }

    public sealed class ProbeResult
    {
        public InstallState State { get; init; }
        public int? CurrentBuild { get; init; }
        public string Detail { get; init; } = "";

        public bool NeedsInstall => State != InstallState.Good;
    }

    public static string CoreDllPath(string gameRoot) =>
        Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");

    public static ProbeResult Probe(string gameRoot)
    {
        var core = CoreDllPath(gameRoot);
        if (!File.Exists(core))
            return new ProbeResult
            {
                State = InstallState.Absent,
                Detail = "BepInEx 6 IL2CPP not found (nothing to upgrade)",
            };

        int? build = null;
        try
        {
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(core).ProductVersion ?? "";
            var m = Regex.Match(v, @"-be\.(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var b)) build = b;
        }
        catch (Exception)
        {
            // unreadable metadata; treat as unknown
        }

        if (build.HasValue && build.Value >= BuildInfo.BepInExBuild)
            return new ProbeResult
            {
                State = InstallState.Good,
                CurrentBuild = build,
                Detail = $"BepInEx be.{build} installed (>= be.{BuildInfo.BepInExBuild} required)",
            };

        return new ProbeResult
        {
            State = InstallState.UpgradeNeeded,
            CurrentBuild = build,
            Detail = build.HasValue
                ? $"BepInEx be.{build} is older than the required be.{BuildInfo.BepInExBuild}; upgrading"
                : "BepInEx build number unknown; installing the verified be.{BuildInfo.BepInExBuild} build",
        };
    }

    /// <summary>
    /// Ensures a supported BepInEx is present. Downloads once into the local app
    /// data cache, then overlays into the game root.
    /// </summary>
    public static async Task EnsureInstalledAsync(
        string gameRoot,
        Action<string> status,
        Action<int> progress,
        CancellationToken ct)
    {
        var probe = Probe(gameRoot);
        if (probe.State == InstallState.Good) return;

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GWYFCommunityNetwork");
        Directory.CreateDirectory(cacheDir);
        var zipPath = Path.Combine(cacheDir, $"bepinex-be{BuildInfo.BepInExBuild}.zip");

        if (!File.Exists(zipPath))
        {
            status($"Downloading BepInEx be.{BuildInfo.BepInExBuild} (~34 MB)…");
            long total = 0;
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using (var response = await client.GetAsync(BuildInfo.BepInExZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                total = response.Content.Headers.ContentLength ?? 0;
                using var src = await response.Content.ReadAsStreamAsync();
                using var dst = new FileStream(zipPath + ".part", FileMode.Create, FileAccess.Write);
                var buffer = new byte[64 * 1024];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n, ct);
                    read += n;
                    if (total > 0) progress((int)(read * 100 / total));
                }
            }
            if (total > 0 && new FileInfo(zipPath + ".part").Length != total)
                throw new IOException($"download incomplete: expected {total} bytes");
            File.Delete(zipPath);
            File.Move(zipPath + ".part", zipPath);
            VerifyOrThrow(zipPath);
        }
        else
        {
            // Cached copy must still match the pin; a corrupted or tampered cache
            // is irrelevant — it must never reach the game.
            VerifyOrThrow(zipPath);
        }

        status($"Installing BepInEx be.{BuildInfo.BepInExBuild}…");
        await Task.Run(() => ExtractOverlay(zipPath, gameRoot, progress, ct), ct);
    }

    /// <summary>
    /// Supply-chain gate: the pinned SHA-256 is the only archive this installer
    /// will ever extract into a user's game. Any mismatch is fatal.
    /// </summary>
    private static void VerifyOrThrow(string zipPath)
    {
        string actual;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(zipPath))
        {
            actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        if (!string.Equals(actual, BuildInfo.BepInExZipSha256, StringComparison.Ordinal))
        {
            try { File.Delete(zipPath); } catch { }
            throw new InvalidDataException(
                $"BepInEx archive hash mismatch (expected {BuildInfo.BepInExZipSha256}, got {actual}); " +
                "download refused. The pinned BepInEx build did not arrive intact.");
        }
    }

    /// <summary>Extracts the BE zip into the game root, overwriting core files.</summary>
    private static void ExtractOverlay(string zipPath, string gameRoot, Action<int> progress, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var total = zip.Entries.Count;
        var done = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/")) continue;
            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(".."))
                throw new InvalidDataException($"unsafe zip entry: {entry.FullName}");

            var dest = Path.Combine(gameRoot, entry.FullName);
            var dir = Path.GetDirectoryName(dest);
            if (dir != null) Directory.CreateDirectory(dir);

            using (var src = entry.Open())
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);

            done++;
            if (done % 20 == 0) progress(done * 100 / total);
        }
        progress(100);
    }
}
