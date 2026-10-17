using System.Text.Json;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using CleanSpace.Infrastructure.Settings;
using Xunit;

namespace CleanSpace.IntegrationTests;

// Fixtures stay beneath this test assembly's output directory. No deletion or real-user settings.
public sealed class JsonThemeSettingsStoreTests
{
    [Fact]
    public async Task MissingSettingsReturnNullWithoutCreatingDirectory()
    {
        var fixture = new SettingsFixture();

        Assert.Null(await fixture.Store.ReadAsync(CancellationToken.None));
        Assert.False(Directory.Exists(fixture.SettingsDirectory));
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public async Task ThemePersistsAcrossStoreInstances(ThemePreference theme)
    {
        var fixture = new SettingsFixture();
        var expected = new ThemeSettings(theme);

        await fixture.Store.WriteAsync(expected, CancellationToken.None);
        var loaded = await new JsonThemeSettingsStore(fixture.SettingsDirectory).ReadAsync(CancellationToken.None);

        Assert.Equal(expected, loaded);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.Equal(theme.ToString(), json.RootElement.GetProperty("Theme").GetString());
        Assert.Equal("untouched", await File.ReadAllTextAsync(fixture.SentinelPath));
    }

    [Fact]
    public async Task ASecondSaveUpdatesOnlyTheOwnedSettingsFile()
    {
        var fixture = new SettingsFixture();
        await fixture.Store.WriteAsync(new(ThemePreference.Dark), CancellationToken.None);
        await fixture.Store.WriteAsync(new(ThemePreference.Light), CancellationToken.None);

        Assert.Equal(new ThemeSettings(ThemePreference.Light), await fixture.Store.ReadAsync(CancellationToken.None));
        Assert.Equal("untouched", await File.ReadAllTextAsync(fixture.SentinelPath));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"Theme\":123}")]
    public async Task MalformedJsonIsRejectedWithoutChangingTheFile(string contents)
    {
        var fixture = new SettingsFixture();
        await fixture.SeedAsync(contents);

        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ReadAsync(CancellationToken.None));

        Assert.Equal(contents, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Theme\":\"Sepia\"}")]
    [InlineData("{\"Theme\":\"dark\"}")]
    public async Task UnsupportedSettingsAreRejected(string contents)
    {
        var fixture = new SettingsFixture();
        await fixture.SeedAsync(contents);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ReadAsync(CancellationToken.None));

        Assert.Equal(contents, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Fact]
    public async Task OversizedSettingsAreRejectedBeforeDeserializing()
    {
        var fixture = new SettingsFixture();
        await fixture.SeedAsync(new string(' ', 4097));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LockedSettingsProduceReadFailureAndSafeApplicationFallback()
    {
        var fixture = new SettingsFixture();
        await fixture.SeedAsync("{\"Theme\":\"Dark\"}");
        using var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => fixture.Store.ReadAsync(CancellationToken.None));
        var result = await new ThemeSettingsService(fixture.Store).LoadAsync();

        Assert.Equal(ThemePreference.System, result.Settings.Theme);
        Assert.NotNull(result.Notice);
        Assert.DoesNotContain(fixture.Root, result.Notice);
    }

    [Fact]
    public async Task LockedSettingsProduceWriteFailureAndKeepPreviousValue()
    {
        var fixture = new SettingsFixture();
        await fixture.Store.WriteAsync(new(ThemePreference.Dark), CancellationToken.None);
        var service = new ThemeSettingsService(fixture.Store);
        await service.LoadAsync();

        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() =>
                fixture.Store.WriteAsync(new(ThemePreference.Light), CancellationToken.None));
            var result = await service.SaveAsync(ThemePreference.Light);
            Assert.Equal(ThemePreference.Dark, result.Settings.Theme);
            Assert.NotNull(result.Notice);
            Assert.DoesNotContain(fixture.Root, result.Notice);
        }

        Assert.Equal(new ThemeSettings(ThemePreference.Dark), await fixture.Store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DirectoryAtSettingsFilePathProducesWriteFailure()
    {
        var fixture = new SettingsFixture();
        Directory.CreateDirectory(fixture.SettingsPath);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Store.WriteAsync(new(ThemePreference.Dark), CancellationToken.None));

        Assert.True(Directory.Exists(fixture.SettingsPath));
    }

    [Fact]
    public async Task PreCancelledReadDoesNotChangeExistingContents()
    {
        var fixture = new SettingsFixture();
        const string contents = "{\"Theme\":\"Light\"}";
        await fixture.SeedAsync(contents);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.ReadAsync(cancellation.Token));

        Assert.Equal(contents, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotCreateSettingsDirectory()
    {
        var fixture = new SettingsFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Store.WriteAsync(new(ThemePreference.Dark), cancellation.Token));

        Assert.False(Directory.Exists(fixture.SettingsDirectory));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotOverwriteExistingContents()
    {
        var fixture = new SettingsFixture();
        const string contents = "{\"Theme\":\"Dark\"}";
        await fixture.SeedAsync(contents);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Store.WriteAsync(new(ThemePreference.Light), cancellation.Token));

        Assert.Equal(contents, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Fact]
    public void RelativeFixtureDirectoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => new JsonThemeSettingsStore("relative-settings"));

    private sealed class SettingsFixture
    {
        public SettingsFixture()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "fixtures", "theme-settings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "fixture-owner.txt"), "CleanSpace.IntegrationTests");
            SentinelPath = Path.Combine(Root, "sentinel.txt");
            File.WriteAllText(SentinelPath, "untouched");
            SettingsDirectory = Path.Combine(Root, "settings");
            SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
            Store = new JsonThemeSettingsStore(SettingsDirectory);
        }

        public string Root { get; }
        public string SettingsDirectory { get; }
        public string SettingsPath { get; }
        public string SentinelPath { get; }
        public JsonThemeSettingsStore Store { get; }

        public async Task SeedAsync(string contents)
        {
            Directory.CreateDirectory(SettingsDirectory);
            await File.WriteAllTextAsync(SettingsPath, contents);
        }
    }
}
