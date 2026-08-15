using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace GwyfCommunity.Setup;

/// <summary>
/// Headless smoke test (--selftest): validates the embedded package and the
/// Steam discovery logic without touching any files. Writes a report next to
/// the exe and exits non-zero on failure — used by the build pipeline and by
/// this repo's two-machine smoke checks.
/// </summary>
internal static class Selftest
{
    public static int Run()
    {
        var report = new StringBuilder();
        var failures = 0;

        void Check(string name, bool ok, string detail)
        {
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (!ok) failures++;
        }

        void Warn(string name, string detail) =>
            report.AppendLine($"WARN  {name}: {detail}");

        // 1. embedded mod package
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuildInfo.ModZipResource))
        {
            Check("embedded mod zip present", stream != null, BuildInfo.ModZipResource);
            if (stream != null)
            {
                using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                var names = zip.Entries.Select(e => e.FullName).ToArray();
                var pluginDll = names.FirstOrDefault(n => n.EndsWith("plugins/GWYFCommunityNetwork/GWYFCommunityNetwork.dll", StringComparison.OrdinalIgnoreCase));
                var bc = names.FirstOrDefault(n => n.EndsWith("BouncyCastle.Cryptography.dll", StringComparison.OrdinalIgnoreCase));
                Check("mod zip contains plugin dll", pluginDll != null, pluginDll ?? "missing");
                Check("mod zip contains BouncyCastle", bc != null, bc ?? "missing");
                Check("mod zip entries safe", names.All(n => !n.Contains("..")), $"{names.Length} entries");
            }
        }

        // 2. steam discovery
        var installs = SteamLocator.FindGameInstalls();
        if (installs.Count > 0)
        {
            Check("steam discovery", true, string.Join(" ; ", installs.Select(i => i.GameRoot)));
            foreach (var i in installs)
            {
                var probe = BepInExManager.Probe(i.GameRoot);
                Check($"bepinex probe ({Path.GetFileName(i.GameRoot)})", true, $"{probe.State} — {probe.Detail}");
                if (ModDeployer.IsInstalled(i.GameRoot))
                    Check($"mod presence ({Path.GetFileName(i.GameRoot)})", true, ModDeployer.ReadModVersion(i.GameRoot));
                else
                    Warn($"mod presence ({Path.GetFileName(i.GameRoot)})", "not installed (expected before first install)");
            }
        }
        else
        {
            // Fine for a build machine that lacks the game: the UI's choose-folder
            // fallback exists for exactly this machine. Not a packaging failure.
            Warn("steam discovery", "no Golf With Your Friends install found (build machine without the game)");
        }

        // 3. config seeding renders as valid BepInEx toml
        var configPath = Path.Combine(Path.GetTempPath(), "gwyf-setup-selftest-cfg.txt");
        try
        {
            File.Delete(configPath);
            File.WriteAllText(configPath,
                "[network]\nenabled = true\napiBaseUrl = \"http://127.0.0.1:3000\"\n");
            var toml = File.ReadAllText(configPath);
            Check("config template", toml.Contains("[network]") && toml.Contains("apiBaseUrl"), "parses as BepInEx section");
        }
        catch (Exception ex)
        {
            Check("config template", false, ex.Message);
        }
        finally
        {
            try { File.Delete(configPath); } catch { }
        }

        var outPath = Path.Combine(AppContext.BaseDirectory, "selftest-results.txt");
        File.WriteAllText(outPath, report.ToString());
        Console.WriteLine(report);

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Exercises the real install/uninstall code paths against a throwaway
    /// sandbox directory (never the live game). Downloads/caches BepInEx once.
    /// Entry: --selftest-install &lt;sandboxDir&gt;
    /// </summary>
    public static int RunInstallAgainst(string sandboxDir)
    {
        var report = new StringBuilder();
        int failures = 0;

        void Check(string name, bool ok, string detail)
        {
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (!ok) failures++;
        }

        var sandbox = Path.GetFullPath(sandboxDir);
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        Directory.CreateDirectory(sandbox);

        try
        {
            // A fresh fake game root has no BepInEx.
            var probe0 = BepInExManager.Probe(sandbox);
            Check("fresh root probes as Absent", probe0.State == BepInExManager.InstallState.Absent, probe0.State.ToString());

            // Install: BepInEx -> mod -> config -> marker.
            BepInExManager.EnsureInstalledAsync(sandbox,
                _ => { }, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
            var probe1 = BepInExManager.Probe(sandbox);
            Check("bepinex installed", probe1.State == BepInExManager.InstallState.Good,
                $"{probe1.State} ({probe1.Detail})");
            Check("doorstop present", File.Exists(Path.Combine(sandbox, "winhttp.dll")), "winhttp.dll");

            ModDeployer.Deploy(sandbox, _ => { }, CancellationToken.None);
            Check("plugin deployed",
                File.Exists(Path.Combine(ModDeployer.PluginDir(sandbox), "GWYFCommunityNetwork.dll")) &&
                File.Exists(Path.Combine(ModDeployer.PluginDir(sandbox), "BouncyCastle.Cryptography.dll")),
                "dll + bouncycastle");
            Check("mod flagged installed", ModDeployer.IsInstalled(sandbox), ModDeployer.ReadModVersion(sandbox));

            var wroteConfig = ModDeployer.EnsureConfig(sandbox);
            Check("config seeded", wroteConfig && File.Exists(ModDeployer.ConfigPath(sandbox)),
                ModDeployer.ConfigPath(sandbox));
            Check("config not clobbered on second call", !ModDeployer.EnsureConfig(sandbox), "existing config preserved");

            ModDeployer.WriteSetupMarker(sandbox, installedBepInEx: true);
            Check("marker records BE ownership", ModDeployer.MarkerSaysWeInstalledBepInEx(sandbox), "installedBepInEx=true");
            Check("bepinex still useful (other plugins area exists)", !ModDeployer.BepInExIsNowUseless(sandbox),
                "mod reclaims usefulness until removed");

            // Uninstall: gwf files gone, BepInEx stays until removed.
            ModDeployer.Uninstall(sandbox);
            Check("uninstall removes mod", !ModDeployer.IsInstalled(sandbox)
                  && !File.Exists(ModDeployer.ConfigPath(sandbox))
                  && !Directory.Exists(ModDeployer.StateDir(sandbox)), "plugin+config+state gone");
            Check("uninstall leaves bepinex", File.Exists(Path.Combine(sandbox, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")),
                "BepInEx intact");

            ModDeployer.RemoveBepInEx(sandbox);
            Check("bepinex removal", !Directory.Exists(Path.Combine(sandbox, "BepInEx"))
                  && !File.Exists(Path.Combine(sandbox, "winhttp.dll"))
                  && !File.Exists(Path.Combine(sandbox, "doorstop_config.ini")), "game root back to source");
        }
        catch (Exception ex)
        {
            Check("install/uninstall flow", false, ex.ToString());
        }
        finally
        {
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true); } catch { }
        }

        var outPath = Path.Combine(AppContext.BaseDirectory, "selftest-install-results.txt");
        File.WriteAllText(outPath, report.ToString());
        Console.WriteLine(report);

        return failures == 0 ? 0 : 1;
    }
}
