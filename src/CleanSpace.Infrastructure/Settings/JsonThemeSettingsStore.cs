using System.Text.Json;
using CleanSpace.Application.Abstractions;
using CleanSpace.Core.Models;

namespace CleanSpace.Infrastructure.Settings;

// This adapter accesses only CleanSpace's own appearance preference; it never enumerates files.
public sealed class JsonThemeSettingsStore : IThemeSettingsStore
{
    private const int MaximumSettingsBytes = 4096;
    private readonly string settingsDirectory;
    private readonly string settingsPath;

    public JsonThemeSettingsStore() : this(GetDefaultSettingsDirectory())
    {
    }

    // Tests provide an isolated application-owned directory, never the user's real settings.
    internal JsonThemeSettingsStore(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        if (!Path.IsPathFullyQualified(settingsDirectory))
        {
            throw new ArgumentException("The settings directory must be absolute.", nameof(settingsDirectory));
        }

        this.settingsDirectory = settingsDirectory;
        settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    private static string GetDefaultSettingsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData))
        {
            throw new InvalidOperationException("Local application settings are unavailable.");
        }

        return Path.Combine(localAppData, "CleanSpace");
    }

    public async Task<ThemeSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExistingPath();

        try
        {
            await using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumSettingsBytes)
            {
                throw new InvalidDataException("Appearance settings exceed the supported size.");
            }

            var stored = await JsonSerializer.DeserializeAsync<StoredSettings>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (stored?.Theme is not ("System" or "Light" or "Dark"))
            {
                throw new InvalidDataException("Appearance settings contain an unknown theme.");
            }

            return new ThemeSettings(Enum.Parse<ThemePreference>(stored.Theme));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task WriteAsync(ThemeSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExistingPath();
        Directory.CreateDirectory(settingsDirectory);
        ValidateExistingPath();

        // No temporary-file deletion or replace operation: only this app-owned setting is written.
        await using var stream = new FileStream(settingsPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, new StoredSettings(settings.Theme.ToString()),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void ValidateExistingPath()
    {
        // Reject redirected existing components instead of following a settings-directory junction.
        for (var directory = new DirectoryInfo(settingsDirectory); directory is not null;
             directory = directory.Parent)
        {
            RejectReparsePoint(directory.FullName);
        }

        RejectReparsePoint(settingsPath);
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Redirected appearance settings are unsupported.");
            }
        }
        catch (FileNotFoundException)
        {
            // Missing settings are allowed on first launch.
        }
        catch (DirectoryNotFoundException)
        {
            // Missing app settings directories are created only when saving.
        }
    }

    private sealed record StoredSettings(string? Theme);
}
