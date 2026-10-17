using System.Security;
using System.Text.Json;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Core.Models;

namespace CleanSpace.Application.UseCases;

public sealed class ThemeSettingsService(IThemeSettingsStore store)
{
    private ThemeSettings current = ThemeSettings.Default;

    public async Task<ThemeSettingsResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            current = await store.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? ThemeSettings.Default;
            return new(current);
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            current = ThemeSettings.Default;
            return new(current, "Saved appearance could not be read. Using the system theme.");
        }
    }

    public async Task<ThemeSettingsResult> SaveAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = new ThemeSettings(preference);

        try
        {
            await store.WriteAsync(settings, cancellationToken).ConfigureAwait(false);
            current = settings;
            return new(current);
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            return new(current, "Appearance could not be saved. The previous theme is still active.");
        }
    }

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException
            or JsonException or InvalidDataException or NotSupportedException;
}
