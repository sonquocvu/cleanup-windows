using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.Core.Tests;

public sealed class ThemeSettingsTests
{
    [Fact]
    public void DefaultsToSystemTheme() => Assert.Equal(ThemePreference.System, ThemeSettings.Default.Theme);

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void AcceptsSupportedThemes(ThemePreference preference) =>
        Assert.Equal(preference, new ThemeSettings(preference).Theme);

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RejectsUndefinedThemes(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThemeSettings((ThemePreference)value));
}
