using System.IO.Compression;

namespace GwyfCommunity.Setup;

/// <summary>Shared zip extraction with path-traversal guards and progress.</summary>
internal static class ZipTool
{
    public static void Extract(Stream zipStream, string destRoot, Action<int> progress, CancellationToken ct)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var total = zip.Entries.Count;
        var done = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/")) continue;
            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(".."))
                throw new InvalidDataException($"unsafe zip entry: {entry.FullName}");

            var dest = Path.Combine(destRoot, entry.FullName);
            var dir = Path.GetDirectoryName(dest);
            if (dir != null) Directory.CreateDirectory(dir);

            using (var src = entry.Open())
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);

            done++;
            if (done % 5 == 0) progress(done * 100 / total);
        }
        progress(100);
    }
}
