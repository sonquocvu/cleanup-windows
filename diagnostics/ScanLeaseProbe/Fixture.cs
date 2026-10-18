using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ScanSharingProbe;

namespace ScanLeaseProbe;

// All mutation belongs to fixture plumbing; the lease/cursor code has no mutation API.
internal sealed class Fixture
{
    internal static string Workspace
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CleanSpace.slnx")))
            { directory = directory.Parent; }
            return directory?.FullName ?? throw new IOException("Repository missing.");
        }
    }
    internal static string Artifacts => Path.Combine(Workspace, "artifacts", "task-003-rework-02");
    internal static string Fixtures => Path.Combine(Artifacts, "fixtures");
    internal string Owner { get; }
    internal const string Target = @"ancestor\Temp\child";
    internal Fixture(int files = 1)
    {
        ValidateAncestry();
        Owner = Path.Combine(Fixtures, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Owner, Target));
        Directory.CreateDirectory(Path.Combine(Owner, "outside"));
        File.WriteAllText(Path.Combine(Owner, "owner.marker"), "TASK-003-REWORK-02 owned fixture; retained.");
        for (var i = 0; i < files; i++)
        { File.WriteAllBytes(Path.Combine(Owner, Target, $"file-{i:D4}.bin"), new byte[i % 3]); }
    }
    internal static string Resolve(string owner, string relative, bool allowLeafReparse = false)
    {
        ValidateAncestry();
        owner = Path.GetFullPath(owner);
        if (!string.Equals(Path.GetDirectoryName(owner), Fixtures, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(owner), "N", out _)
            || !File.Exists(Path.Combine(owner, "owner.marker")))
        { throw new IOException("Unowned fixture refused."); }
        var path = Path.GetFullPath(Path.Combine(owner, relative));
        if (path != owner && !path.StartsWith(owner + "\\", StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Fixture escape refused."); }
        var current = path;
        while (current.Length >= owner.Length)
        {
            if (!(allowLeafReparse && current == path)
                && (File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Reparse fixture path refused."); }
            current = Path.GetDirectoryName(current) ?? "";
        }
        return path;
    }
    private static void ValidateAncestry()
    {
        var current = Path.GetFullPath(Fixtures);
        if (!current.StartsWith(Path.GetFullPath(Workspace) + "\\", StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Artifacts escaped repository."); }
        while (current.Length >= Workspace.Length)
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Reparse artifact ancestor."); }
            current = Path.GetDirectoryName(current) ?? "";
        }
    }

    internal sealed record Reply(int Pid, int Error, string? Detail = null);
    internal static int Child(string[] args)
    {
        try
        {
            var owner = args[2];
            var path = Resolve(owner, args[3]);
            switch (args[1])
            {
                case "open":
                    using (NativeProbe.Open(path, Convert.ToUInt32(args[4], 16), 7)) { }
                    break;
                case "move":
                    var to = Resolve(owner, args[4]);
                    if (path == owner || to == owner) { throw new IOException("Cannot move fixture owner."); }
                    Directory.Move(path, to);
                    break;
                case "mkdir": Directory.CreateDirectory(path); break;
                case "create":
                    using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
                    break;
                case "junction":
                    var target = Resolve(owner, args[4]);
                    var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true };
                    // Both arguments are resolved owned paths. This creates a junction only; no cleanup shell command.
                    start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink");
                    start.ArgumentList.Add("/J"); start.ArgumentList.Add(path); start.ArgumentList.Add(target);
                    using (var process = Process.Start(start)!)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (process.ExitCode != 0) { throw new IOException($"Junction creation failed: {output} {error}"); }
                    }
                    break;
                case "symlink": Directory.CreateSymbolicLink(path, Resolve(owner, args[4])); break;
                default: throw new IOException("Unknown child action.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new Reply(Environment.ProcessId, 0)));
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new Reply(Environment.ProcessId,
                error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xFFFF, error.Message)));
        }
        return 0;
    }

    internal async Task<Reply> Other(string operation, string relative, string extra = "")
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "--child", operation, Owner, relative, extra })
        { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start) ?? throw new IOException("Child process missing.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (process.ExitCode != 0) { throw new IOException(await errors); }
        var reply = JsonSerializer.Deserialize<Reply>(await output) ?? throw new IOException("Missing reply.");
        if (reply.Pid == Environment.ProcessId) { throw new IOException("Concurrency requires another process."); }
        return reply;
    }
}
