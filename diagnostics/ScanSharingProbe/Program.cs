using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ScanSharingProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<object> Evidence = [];
    private static int checks;

    private static async Task<int> Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException(); }
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (principal.IsInRole(WindowsBuiltInRole.Administrator)) { throw new InvalidOperationException("Use a normal user token."); }
        if (arguments.FirstOrDefault() == "--child") { return Child(arguments); }
        Directory.CreateDirectory(OwnedFixture.Artifacts);
        var report = Path.Combine(OwnedFixture.Artifacts, "sharing-probe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await SharingMatrix(false);
            await SharingMatrix(true);
            await PersistentRelocation(3); // Child enumeration directory.
            await PersistentRelocation(1); // Held ancestor containing the Temp root.
            await PersistentRelocation(2); // Temp root itself.
            await Replacement();
            await ValidationQueryRace();
            await OutAndBackRace();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Status = "PASS", Checks = checks, Evidence }, Json));
            Console.WriteLine(JsonSerializer.Serialize(new { Status = "PASS", Checks = checks, Report = report }, Json));
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Status = "FAIL", Checks = checks,
                Error = error.ToString(), Evidence }, Json));
            Console.Error.WriteLine(error);
            Console.WriteLine(report);
            return 1;
        }
    }

    private static int Child(string[] args)
    {
        var operation = args[1];
        var owner = args[2];
        var path = OwnedFixture.Resolve(owner, args[3]);
        var result = 0;
        try
        {
            if (operation == "open")
            {
                using var handle = NativeProbe.Open(path, Convert.ToUInt32(args[4], 16), 7);
            }
            else if (operation == "rename")
            {
                var destination = OwnedFixture.Resolve(owner, args[4]);
                // Both resolved absolute paths are checked inside the same unique owner before any move.
                if (path == Path.GetFullPath(owner) || destination == Path.GetFullPath(owner))
                { throw new IOException("Cannot relocate fixture owner."); }
                Directory.Move(path, destination);
            }
            else if (operation == "mkdir") { Directory.CreateDirectory(path); }
            else { throw new IOException("Unknown fixture action."); }
        }
        catch (Win32Exception error) { result = error.NativeErrorCode; }
        catch (IOException error) { result = error.HResult & 0xFFFF; }
        catch (UnauthorizedAccessException error) { result = error.HResult & 0xFFFF; }
        Console.WriteLine(JsonSerializer.Serialize(new ChildResult(Environment.ProcessId, result)));
        return 0;
    }

    private sealed record ChildResult(int Pid, int Error);

    private static async Task<ChildResult> OtherProcess(OwnedFixture fixture, string operation, string relative, string extra = "")
    {
        var start = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "--child", operation, fixture.Owner, relative, extra })
        { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start) ?? throw new IOException("Fixture child failed to launch.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Require(process.ExitCode == 0, "Fixture child failed: " + await errors);
        var result = JsonSerializer.Deserialize<ChildResult>(await output) ?? throw new IOException("No child result.");
        Require(result.Pid != Environment.ProcessId, "Must be a separate process.");
        return result;
    }

    private static async Task SharingMatrix(bool fullSharing)
    {
        using var fixture = new OwnedFixture(fullSharing);
        var modes = new[] { ("ReadAttributes", 0x80u), ("ListDirectory", 1u), ("AddFile", 2u),
            ("WriteAttributes", 0x100u), ("DeleteAccessOnly", 0x10000u) };
        foreach (var (role, relative) in new[] { ("VolumeRootStyle", "."), ("Ancestor", "ancestor"),
            ("TempRoot", @"ancestor\Temp"), ("Child", @"ancestor\Temp\child") })
        foreach (var (access, mask) in modes)
        {
            var result = await OtherProcess(fixture, "open", relative, mask.ToString("X"));
            Evidence.Add(new { Scenario = "Sharing", FullSharing = fullSharing, Role = role, Access = access,
                Mask = $"0x{mask:X}", result.Pid, result.Error });
            Require(result.Error is 0 or 32, "Unexpected native open error.");
            if (fullSharing || mask == 0x80) { Require(result.Error == 0, "Compatible open refused."); }
        }
        using var file = fullSharing ? NativeProbe.CandidateChild(fixture.Handles[^1], "file.bin", false)
            : NativeProbe.Production<SafeFileHandle>("OpenChild", fixture.Handles[^1], "file.bin", false);
        foreach (var (access, mask) in modes.Where(mode => mode.Item2 != 1))
        {
            var result = await OtherProcess(fixture, "open", @"ancestor\Temp\child\file.bin", mask.ToString("X"));
            Evidence.Add(new { Scenario = "Sharing", FullSharing = fullSharing, Role = "FileMetadata", Access = access,
                Mask = $"0x{mask:X}", result.Pid, result.Error });
            Require(result.Error == 0, "Attribute-only scanner file handle caused a conflict.");
        }
    }

    private static async Task PersistentRelocation(int level)
    {
        using var fixture = new OwnedFixture(true);
        var from = fixture.RelativePaths[level];
        var result = await OtherProcess(fixture, "rename", from, @"outside\relocated");
        if (result.Error != 0)
        {
            Require(result.Error is 5 or 32, "Unexpected directory relocation failure.");
            fixture.Directories[^1].Validate();
            Evidence.Add(new { Scenario = "PersistentRelocation", Level = level, result.Pid, result.Error,
                RenameSucceeded = false, Note = "Windows refused this relocation; not counted as scanner detection." });
            return;
        }
        ExpectRejected(() => fixture.Directories[^1].Validate());
        ExpectRejected(() => fixture.Directories[^1].ReadNext());
        Evidence.Add(new { Scenario = "PersistentRelocation", Level = level, result.Pid,
            RenameSucceeded = true, ValidateRejected = true, ReadNextRejected = true });
    }

    private static async Task Replacement()
    {
        using var fixture = new OwnedFixture(true);
        var root = fixture.Directories[2];
        var listed = root.ReadNext()!;
        Require(listed.Name == "child", "Unexpected fixture entry.");
        await OtherProcess(fixture, "rename", @"ancestor\Temp\child", @"outside\original");
        await OtherProcess(fixture, "mkdir", @"ancestor\Temp\child");
        ExpectRejected(() => root.OpenDirectory(listed));
        ExpectRejected(() => fixture.Directories[^1].ReadNext());
        Evidence.Add(new { Scenario = "Replacement", OldIdentityRejected = true, HeldDirectoryRejected = true });
    }

    private static async Task ValidationQueryRace()
    {
        using var fixture = new OwnedFixture(true);
        var directory = fixture.Directories[^1];
        directory.Validate();
        await OtherProcess(fixture, "rename", @"ancestor\Temp\child", @"outside\relocated");
        await OtherProcess(fixture, "mkdir", @"outside\relocated\outside-only-entry");
        // Exact current ReadNext boundary: Validate(); [concurrent rename]; ReadDirectory(handle).
        // Do not use ReadNext here: this deliberately controls the scheduling gap between its two calls.
        var names = NativeNames(fixture.Handles[^1]);
        Require(names.Contains("outside-only-entry"), "Handle query did not expose the relocated directory.");
        ExpectRejected(directory.Validate);
        Evidence.Add(new { Scenario = "ValidationQueryGap", EnumeratedOutsideTrustedPath = true,
            LaterValidationRejected = true, Names = names });
    }

    private static async Task OutAndBackRace()
    {
        using var fixture = new OwnedFixture(true);
        var directory = fixture.Directories[^1];
        directory.Validate();
        await OtherProcess(fixture, "rename", @"ancestor\Temp\child", @"outside\relocated");
        await OtherProcess(fixture, "mkdir", @"outside\relocated\inserted-while-outside");
        var names = NativeNames(fixture.Handles[^1]);
        await OtherProcess(fixture, "rename", @"outside\relocated", @"ancestor\Temp\child");
        directory.Validate(); // Same ID/path/attributes again: even an added post-check would pass.
        Require(names.Contains("inserted-while-outside"), "Out-and-back fixture witness missing.");
        Evidence.Add(new { Scenario = "OutAndBack", BeforeValidationPassed = true, AfterValidationPassed = true,
            EnumeratedWhileOutside = true, Names = names });
    }

    private static List<string> NativeNames(SafeFileHandle handle)
    {
        var buffer = new byte[65536];
        Require(NativeProbe.Production<bool>("ReadDirectory", handle, buffer, true), "Fixture enumeration failed.");
        var names = new List<string>();
        var offset = 0;
        do
        {
            var next = BitConverter.ToInt32(buffer, offset);
            var length = BitConverter.ToInt32(buffer, offset + 60);
            Require(length > 0 && length <= 510 && offset + 104 + length <= buffer.Length, "Invalid fixture record.");
            names.Add(Encoding.Unicode.GetString(buffer, offset + 104, length));
            if (next == 0) { break; }
            Require(next >= 104 && next % 8 == 0, "Invalid fixture record offset.");
            offset = checked(offset + next);
        } while (offset <= buffer.Length - 104);
        return names;
    }

    private static void ExpectRejected(Action action)
    {
        try { action(); }
        catch (IOException) { checks++; return; }
        throw new IOException("Uncertain directory was accepted.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new IOException(message); }
        checks++;
    }
}
