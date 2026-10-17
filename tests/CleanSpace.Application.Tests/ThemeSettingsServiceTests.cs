using System.Text.Json;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.Application.Tests;

public sealed class ThemeSettingsServiceTests
{
    [Fact]
    public async Task MissingSettingsUseSystemTheme()
    {
        var result = await new ThemeSettingsService(new MemoryStore()).LoadAsync();

        Assert.Equal(ThemePreference.System, result.Settings.Theme);
        Assert.Null(result.Notice);
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public async Task SavedThemeCanBeLoadedByANewSession(ThemePreference preference)
    {
        var store = new MemoryStore();
        var saved = await new ThemeSettingsService(store).SaveAsync(preference);
        var loaded = await new ThemeSettingsService(store).LoadAsync();

        Assert.Equal(preference, saved.Settings.Theme);
        Assert.Equal(saved.Settings, loaded.Settings);
        Assert.Null(saved.Notice);
        Assert.Null(loaded.Notice);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("io")]
    [InlineData("json")]
    [InlineData("invalid")]
    public async Task UnreadableSettingsFallBackWithoutExposingPaths(string failure)
    {
        var store = new MemoryStore { ReadFailure = CreateFailure(failure) };
        var result = await new ThemeSettingsService(store).LoadAsync();

        Assert.Equal(ThemePreference.System, result.Settings.Theme);
        Assert.NotNull(result.Notice);
        Assert.DoesNotContain("private-path", result.Notice);
        Assert.Equal(0, store.WriteCount);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("io")]
    public async Task FailedSaveKeepsPreviouslyLoadedTheme(string failure)
    {
        var store = new MemoryStore
        {
            Value = new(ThemePreference.Dark),
            WriteFailure = CreateFailure(failure)
        };
        var service = new ThemeSettingsService(store);
        await service.LoadAsync();

        var result = await service.SaveAsync(ThemePreference.Light);

        Assert.Equal(ThemePreference.Dark, result.Settings.Theme);
        Assert.NotNull(result.Notice);
        Assert.DoesNotContain("private-path", result.Notice);
        Assert.Equal(ThemePreference.Dark, store.Value.Theme);
    }

    [Fact]
    public async Task InvalidThemeIsRejectedBeforeWriting()
    {
        var store = new MemoryStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ThemeSettingsService(store).SaveAsync((ThemePreference)99));

        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotAccessStore()
    {
        var store = new MemoryStore();
        var service = new ThemeSettingsService(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(ThemePreference.Dark, cancellation.Token));

        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task StoreCancellationPropagates()
    {
        var store = new MemoryStore { ReadFailure = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ThemeSettingsService(store).LoadAsync());
    }

    private static Exception CreateFailure(string failure) => failure switch
    {
        "access" => new UnauthorizedAccessException("private-path"),
        "io" => new IOException("private-path"),
        "json" => new JsonException("private-path"),
        _ => new InvalidDataException("private-path")
    };

    private sealed class MemoryStore : IThemeSettingsStore
    {
        public ThemeSettings? Value { get; set; }
        public Exception? ReadFailure { get; init; }
        public Exception? WriteFailure { get; init; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public Task<ThemeSettings?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return ReadFailure is null ? Task.FromResult(Value) : Task.FromException<ThemeSettings?>(ReadFailure);
        }

        public Task WriteAsync(ThemeSettings settings, CancellationToken cancellationToken)
        {
            WriteCount++;
            if (WriteFailure is not null)
            {
                return Task.FromException(WriteFailure);
            }

            Value = settings;
            return Task.CompletedTask;
        }
    }
}
