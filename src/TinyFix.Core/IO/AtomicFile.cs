// Atomic file writes: a crash or a full disk never leaves a half-written settings file behind.

using System.Text;

namespace TinyFix.Core.IO;

/// <summary>Writes files through a temporary file and a rename.</summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Replaces <paramref name="path"/> with <paramref name="text"/> (UTF-8, no byte order mark).</summary>
    public static void WriteAllText(string path, string text) => WriteAllBytes(path, Utf8NoBom.GetBytes(text));

    /// <summary>Replaces <paramref name="path"/> with <paramref name="bytes"/>.</summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        // Same directory as the target so the final move stays on one volume and is atomic.
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
