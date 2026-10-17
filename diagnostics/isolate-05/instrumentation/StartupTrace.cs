namespace CleanSpace.App;

internal static class StartupTrace
{
    private static readonly StreamWriter Writer = CreateWriter();

    private static StreamWriter CreateWriter()
    {
        var root = Environment.GetEnvironmentVariable("CLEANSPACE_ISOLATE05_LOGS");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
        {
            throw new InvalidOperationException("A controlled diagnostic directory is required.");
        }
        return new StreamWriter(new FileStream(Path.Combine(root, $"startup-{Environment.ProcessId}.log"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough)) { AutoFlush = true };
    }

    internal static void Mark(string stage)
    {
        var line = $"Isolate: {stage}";
        Writer.WriteLine($"{DateTimeOffset.Now:O} {line}");
        Console.Error.WriteLine(line);
    }

    internal static void Failure(string stage, Exception exception)
    {
        Mark($"{stage} exception {exception.GetType().FullName}, HRESULT 0x{exception.HResult:X8}");
        Writer.WriteLine(exception.ToString());
    }
}
