using CleanSpace.Core.Models;

namespace CleanSpace.Application.DTOs;

public sealed record ThemeSettingsResult(ThemeSettings Settings, string? Notice = null);
