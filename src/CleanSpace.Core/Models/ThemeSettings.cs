namespace CleanSpace.Core.Models;

public sealed record ThemeSettings
{
    public static ThemeSettings Default { get; } = new(ThemePreference.System);

    public ThemeSettings(ThemePreference theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        Theme = theme;
    }

    public ThemePreference Theme { get; }
}
