using CleanSpace.Core.Models;

namespace CleanSpace.Application.DTOs;

public sealed record DriveOverviewResult(DriveOverview? Overview, string? Error = null);
