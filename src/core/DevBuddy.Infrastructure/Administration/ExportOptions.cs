namespace DevBuddy.Infrastructure.Administration;

/// <summary>Where a project export is written, and how long it is kept before retention purges it.</summary>
public sealed class ExportOptions
{
    public const string SectionName = "Export";

    /// <summary>Root directory holding one subdirectory per project, then one per export taken.</summary>
    public string RootPath { get; set; } = "./.data/exports";

    /// <summary>How long an export is kept before it is a candidate for deletion (ADR-0009, SB-27).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);
}
