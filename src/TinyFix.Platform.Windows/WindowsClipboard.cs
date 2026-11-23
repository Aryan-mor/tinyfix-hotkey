// The Windows clipboard through the Win32 API: a full snapshot of every memory-backed format, text read and write,
// and the sequence number that tells "the clipboard changed". Ported from app.py (snapshot_clipboard et al.).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="IClipboardAccess"/> over user32.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboard : IClipboardAccess
{
    /// <summary>Formats larger than this are not copied into a snapshot.</summary>
    private const int MaxFormatBytes = 32 * 1024 * 1024;

    private sealed class FormatSnapshot(Dictionary<uint, byte[]> formats) : IClipboardSnapshot
    {
        public Dictionary<uint, byte[]> Formats { get; } = formats;

        public void Dispose()
        {
        }
    }

    /// <inheritdoc />
    public long ChangeCount => NativeMethods.GetClipboardSequenceNumber();

    // Another process can hold the clipboard open for a moment: retry a few times.
    private static bool Open()
    {
        for (var i = 0; i < 8; i++)
        {
            if (NativeMethods.OpenClipboard(0))
                return true;
            Thread.Sleep(20);
        }
        return false;
    }

    /// <inheritdoc />
    public IClipboardSnapshot? Snapshot()
    {
        if (!Open())
            return null;
        try
        {
            var data = new Dictionary<uint, byte[]>();
            uint format = 0;
            while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
            {
                // Bitmaps and metafiles are GDI handles, not memory blocks: they are not snapshotted (as in the Python app).
                if (format is NativeMethods.CfBitmap or NativeMethods.CfMetafilePict or NativeMethods.CfEnhMetafile)
                    continue;
                var handle = NativeMethods.GetClipboardData(format);
                if (handle == 0)
                    continue;
                var size = NativeMethods.GlobalSize(handle);
                if (size == 0 || size > MaxFormatBytes)
                    continue;
                var pointer = NativeMethods.GlobalLock(handle);
                if (pointer == 0)
                    continue;
                try
                {
                    var bytes = new byte[(int)size];
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    data[format] = bytes;
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
            return new FormatSnapshot(data);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <inheritdoc />
    public void Restore(IClipboardSnapshot? snapshot)
    {
        if (snapshot is not FormatSnapshot saved || !Open())
            return;
        try
        {
            NativeMethods.EmptyClipboard();
            foreach (var (format, bytes) in saved.Formats)
                Put(format, bytes);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool Put(uint format, byte[] bytes)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveableZeroInit, (nuint)bytes.Length);
        if (handle == 0)
            return false;
        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == 0)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        NativeMethods.GlobalUnlock(handle);
        // On success the system owns the memory; on failure it is ours to free.
        if (NativeMethods.SetClipboardData(format, handle) == 0)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public string? GetText()
    {
        if (!Open())
            return null;
        try
        {
            if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfUnicodeText))
                return null;
            var handle = NativeMethods.GetClipboardData(NativeMethods.CfUnicodeText);
            if (handle == 0)
                return null;
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <inheritdoc />
    /// <exception cref="IOException">The clipboard could not be opened or written.</exception>
    public void SetText(string text)
    {
        var payload = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        if (!Open())
            throw new IOException("The clipboard is in use by another program.");
        try
        {
            NativeMethods.EmptyClipboard();
            if (!Put(NativeMethods.CfUnicodeText, payload))
                throw new IOException("Could not write the clipboard.");
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (!Open())
            return;
        try
        {
            NativeMethods.EmptyClipboard();
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }
}
