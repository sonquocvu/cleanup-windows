using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CleanSpace.Infrastructure.Scanning;
using Xunit;

namespace CleanSpace.IntegrationTests;

public sealed class ScanApiSafetyTests
{
    [Fact]
    public void NativeImportsAreOnlyTheReviewedMetadataAndHandleApis()
    {
        var imports = Types().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() is not null).Select(m => m.Name).Order().ToArray();
        Assert.Equal(new[] { "CreateFileW", "GetFileInformationByHandle", "GetFileInformationByHandleEx",
            "GetFinalPathNameByHandleW", "NtCreateFile", "SHGetKnownFolderPath" }, imports);
    }

    [Fact]
    public void ScannerHasNoManagedContentMutationProcessNetworkOrRegistryCalls()
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        foreach (var type in Types())
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
                    var name = called.DeclaringType?.FullName ?? "";
                    Assert.DoesNotContain(name, new[] { "System.IO.File", "System.IO.Directory", "System.IO.FileInfo",
                        "System.IO.DirectoryInfo", "System.IO.FileStream", "System.Diagnostics.Process" });
                    Assert.False(name.StartsWith("System.Net.", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal));
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

    private static IEnumerable<Type> Types() => typeof(WindowsTempScopeProvider).Assembly.GetTypes()
        .Where(t => t.Namespace == "CleanSpace.Infrastructure.Scanning");
}
