using CleanSpace.Core.Models;

namespace CleanSpace.Application.Abstractions;

public interface IThemeSettingsStore
{
    Task<ThemeSettings?> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(ThemeSettings settings, CancellationToken cancellationToken);
}
