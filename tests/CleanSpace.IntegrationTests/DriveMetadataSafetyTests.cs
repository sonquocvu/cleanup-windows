using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CleanSpace.Infrastructure.Windows;
using Xunit;

namespace CleanSpace.IntegrationTests;

// Regression guards for the compiled adapter, including compiler-generated Task.Run callbacks.
// These audit its reachable API surface; they are not a host-wide I/O trace.
public sealed class DriveMetadataSafetyTests
{
    [Fact]
    public void NativeImportsAreExclusivelyApprovedReadOnlyMetadataQueries()
    {
        var imports = AdapterTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => (method, import: method.GetCustomAttribute<DllImportAttribute>()))
            .Where(pair => pair.import is not null).ToArray();
        Assert.Equal(new[] { "GetDiskSpaceInformationW", "GetSystemWindowsDirectoryW", "GetVolumeInformationW",
            "GetVolumeNameForVolumeMountPointW", "GetVolumePathNameW" }, imports.Select(pair => pair.method.Name).Order());
        Assert.All(imports, pair => Assert.Equal("kernel32.dll", pair.import!.Value));
    }

    [Fact]
    public void AdapterDoesNotCallEnumerationContentMutationProcessRegistryOrNetworkApis()
    {
        var forbiddenTypes = new[] { typeof(File), typeof(Directory), typeof(FileInfo), typeof(DirectoryInfo), typeof(FileStream) };
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        foreach (var type in AdapterTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (method.DeclaringType != type || method.GetMethodBody()?.GetILAsByteArray() is not { } il) { continue; }
            for (var offset = 0; offset < il.Length;)
            {
                var first = il[offset++];
                var opcode = opcodes[first == 0xFE ? (ushort)(0xFE00 | il[offset++]) : first];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset))!;
                    Assert.DoesNotContain(called.DeclaringType, forbiddenTypes);
                    var name = called.DeclaringType?.FullName ?? "";
                    Assert.False(name.StartsWith("System.Management", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal)
                        || name.StartsWith("System.Net.", StringComparison.Ordinal)
                        || name == "System.Diagnostics.Process", $"Unexpected API: {called}");
                }
                offset += opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
            }
        }
    }

    private static IEnumerable<Type> AdapterTypes() => typeof(WindowsDriveInfoProvider).Assembly.GetTypes()
        .Where(type => type.Namespace == "CleanSpace.Infrastructure.Windows");
}
