using System.Text;
using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace CleanSpace.Infrastructure.Scanning;

internal sealed class WindowsScanDirectory : IScanDirectory
{
    private readonly SafeFileHandle handle;
    private readonly string expectedPath;
    private readonly WindowsScanDirectory? parent;
    private byte[]? buffer;
    private int offset = -1;
    private bool started;
    private bool ended;
    public ScanEntry Identity { get; }

    internal WindowsScanDirectory(SafeFileHandle handle, string expectedPath, string name,
        WindowsScanDirectory? parent = null)
    {
        this.handle = handle;
        this.expectedPath = expectedPath.TrimEnd('\\');
        this.parent = parent;
        try
        {
            Identity = WindowsScanNative.Read(handle, name);
            Validate();
        }
        catch { handle.Dispose(); throw; }
    }

    public void Validate()
    {
        parent?.Validate();
        var now = WindowsScanNative.Read(handle, Identity.Name);
        if (now.Id == 0 || now.Id != Identity.Id || !now.IsDirectory || now.IsUnsafe
            || !string.Equals(expectedPath, WindowsScanNative.FinalPath(handle), StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Directory identity changed."); }
    }

    internal WindowsScanDirectory Open(string name)
    {
        Validate();
        return new(WindowsScanNative.OpenChild(handle, name, true), expectedPath + "\\" + name, name, this);
    }

    public IScanDirectory OpenDirectory(ScanEntry entry)
    {
        var child = Open(entry.Name);
        if (child.Identity.Id == entry.Id) { return child; }
        child.Dispose();
        throw new IOException("Directory replaced.");
    }

    public ScanEntry ReadFileMetadata(ScanEntry entry)
    {
        Validate();
        using var file = WindowsScanNative.OpenChild(handle, entry.Name, false);
        var first = WindowsScanNative.Read(file, entry.Name);
        if (first.IsDirectory || first.IsUnsafe || first.Id != entry.Id
            || !string.Equals(expectedPath + "\\" + entry.Name, WindowsScanNative.FinalPath(file), StringComparison.OrdinalIgnoreCase))
        { throw new IOException("File identity changed."); }
        var second = WindowsScanNative.Read(file, entry.Name);
        Validate();
        if (first != second) { throw new IOException("File metadata changed."); }
        return second;
    }

    public ScanEntry? ReadNext()
    {
        if (ended) { return null; }
        buffer ??= new byte[65536]; // One fixed-size buffer per active depth, never a whole-directory list.
        while (true)
        {
            Validate();
            if (offset < 0)
            {
                if (!WindowsScanNative.ReadDirectory(handle, buffer, !started)) { ended = true; return null; }
                started = true;
                offset = 0;
            }
            var data = buffer.AsSpan(offset);
            if (data.Length < 104) { throw new IOException("Invalid directory record."); }
            var next = BitConverter.ToInt32(data);
            var nameBytes = BitConverter.ToInt32(data[60..]);
            if (nameBytes <= 0 || nameBytes > 510 || (nameBytes & 1) != 0 || nameBytes > data.Length - 104
                || (next != 0 && (next < 104 + nameBytes || next % 8 != 0 || next > data.Length - 104)))
            { throw new IOException("Invalid directory record."); }
            var entry = new ScanEntry(Encoding.Unicode.GetString(data.Slice(104, nameBytes)),
                BitConverter.ToUInt64(data[96..]), BitConverter.ToInt64(data[40..]),
                BitConverter.ToInt64(data[24..]), BitConverter.ToUInt32(data[56..]));
            offset = next == 0 ? -1 : checked(offset + next);
            if (entry.Name is "." or "..") { continue; }
            return entry;
        }
    }

    public void Dispose() => handle.Dispose();
}
