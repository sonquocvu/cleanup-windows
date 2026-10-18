using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace ScanSharingProbe;

internal sealed class OwnedFixture : IDisposable
{
    internal static string Workspace
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CleanSpace.slnx")))
            { directory = directory.Parent; }
            return directory?.FullName ?? throw new IOException("Repository not found.");
        }
    }
    internal static string Artifacts => Path.Combine(Workspace, "artifacts", "task-003-safety-01");
    internal string Owner { get; }
    internal List<SafeFileHandle> Handles { get; } = [];
    internal List<IScanDirectory> Directories { get; } = [];
    internal string[] RelativePaths { get; } = [".", "ancestor", @"ancestor\Temp", @"ancestor\Temp\child"];

    internal OwnedFixture(bool fullSharing)
    {
        ValidateArtifactAncestry();
        Owner = Path.Combine(Artifacts, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Owner, @"ancestor\Temp\child"));
        Directory.CreateDirectory(Path.Combine(Owner, "outside"));
        File.WriteAllText(Path.Combine(Owner, "owner.marker"), "TASK-003-SAFETY-01 test-owned fixture; retained.");
        File.WriteAllBytes(Path.Combine(Owner, @"ancestor\Temp\child\file.bin"), new byte[1]);
        try
        {
            Handles.Add(fullSharing ? NativeProbe.CandidateRoot(Owner) : NativeProbe.Production<SafeFileHandle>("OpenVolume", Owner));
            Directories.Add(NativeProbe.Bind(Handles[^1], "owner", null));
            foreach (var segment in new[] { "ancestor", "Temp", "child" })
            {
                Handles.Add(fullSharing ? NativeProbe.CandidateChild(Handles[^1], segment, true)
                    : NativeProbe.Production<SafeFileHandle>("OpenChild", Handles[^1], segment, true));
                Directories.Add(NativeProbe.Bind(Handles[^1], segment, Directories[^1]));
            }
        }
        catch { Dispose(); throw; }
    }

    internal static string Resolve(string owner, string relative)
    {
        ValidateArtifactAncestry();
        var basePath = Path.GetFullPath(Path.Combine(Artifacts, "fixtures"));
        owner = Path.GetFullPath(owner);
        if (!string.Equals(Path.GetDirectoryName(owner), basePath, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(owner), "N", out _)
            || !File.Exists(Path.Combine(owner, "owner.marker")))
        { throw new IOException("Unowned fixture path refused."); }
        var path = Path.GetFullPath(Path.Combine(owner, relative));
        if (path != owner && !path.StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Fixture escape refused."); }
        var current = path;
        while (current.Length >= owner.Length)
        {
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Fixture reparse path refused."); }
            current = Path.GetDirectoryName(current) ?? "";
        }
        return path;
    }

    private static void ValidateArtifactAncestry()
    {
        var workspace = Path.GetFullPath(Workspace);
        var current = Path.GetFullPath(Path.Combine(Artifacts, "fixtures"));
        if (!current.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Artifacts must remain within the repository."); }
        while (current.Length >= workspace.Length)
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Reparse artifact ancestry refused."); }
            current = Path.GetDirectoryName(current) ?? "";
        }
    }

    public void Dispose()
    {
        foreach (var directory in Directories.AsEnumerable().Reverse()) { directory.Dispose(); }
        foreach (var handle in Handles.AsEnumerable().Reverse()) { handle.Dispose(); }
        // No deletion/cleanup: retain every owned fixture and marker as evidence.
    }
}
