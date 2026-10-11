namespace AdvancedInstallerMcp.Lint;

public enum Severity
{
    // Wrong at 100 %, the scale most developers test with.
    Error,

    // Wrong only at 125 % or 150 %, or a package setting that makes the result less certain.
    Warning,
}

// One DPI's measurement for a text check, in pixels.
public sealed record DpiCheck(int Dpi, int TextWidth, int TextHeight, int AvailableWidth, int AvailableHeight, bool Fits);

// Approximate: measured with a font other than the one the package names (fallback or GDI substitute).
// LowerBound: [Property] references without a value in the package were measured as empty, so the real text is at
// least this long.
// SuggestedWidth / SuggestedHeight: the smallest size in installer units that fits at every checked DPI.
public sealed record Finding(
    string Rule,
    Severity Severity,
    string Message,
    string? Dialog = null,
    string? Control = null,
    string? Font = null,
    bool Approximate = false,
    bool LowerBound = false,
    int? SuggestedWidth = null,
    int? SuggestedHeight = null,
    IReadOnlyList<DpiCheck>? Checks = null);

public sealed record LintReport(string Package, IReadOnlyList<string> Dialogs, IReadOnlyList<Finding> Findings);
