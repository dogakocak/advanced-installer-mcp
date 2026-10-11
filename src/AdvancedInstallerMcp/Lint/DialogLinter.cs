using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Lint;

// lint_dialog's checks (ADR 0003). v0.1: whether the text of Text, CheckBox and PushButton controls fits, and the font
// settings that make that answer less certain. Bounds, overlaps and the tab chain come next.
public static class DialogLinter
{
    // Calibrated 2026-10-11 against msiexec on Windows 11 at 96 DPI with Tahoma 8 pt, four labels each
    // (tools/calibration/calibrate-controls.ps1, docs/research/2026-10-11-control-calibration.md). A check box label
    // wraps inside the control width minus this offset (box + gap); a push button label needs this much more than
    // its text width. Scaled linearly for other DPIs, which is not calibrated.
    public const int CheckBoxTextOffset = 16;
    public const int PushButtonTextMargin = 6;

    // SM_CXEDGE: the 3-D border of a Sunken control, per side.
    public const int SunkenBorder = 2;

    private enum TextKind
    {
        Text,
        CheckBox,
        PushButton,
    }

    // The text as measured: [Property] references replaced by their Property table values (UsesProperties), and by
    // nothing when they have none (LowerBound: the real text is at least this long).
    private sealed record VisibleText(string Text, bool UsesProperties, bool LowerBound);

    public static IReadOnlyList<Finding> Lint(DialogPackage package, IEnumerable<Dialog> dialogs)
    {
        var findings = new List<Finding>();
        var substitutedFaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var anyText = false;

        foreach (var dialog in dialogs)
        {
            foreach (var control in dialog.Controls.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                if (Kind(control) is not { } kind)
                {
                    continue;
                }

                // An EventMapping that sets Text (feature descriptions, progress text) replaces the authored
                // placeholder at run time, so its length says nothing.
                if (control.TextFromEvent)
                {
                    continue;
                }

                var parsed = ControlText.Parse(control.Text, package.Properties);
                var (visible, resolved, unresolved) = FormattedText.Resolve(parsed.DisplayText, package.Properties);
                if (visible.Length == 0)
                {
                    continue;
                }

                anyText = true;
                var font = FontResolver.Resolve(parsed.StyleName, package.TextStyles, package.DefaultUIFont);
                if (font.Issue == FontIssue.TextStyleNotFound)
                {
                    findings.Add(new Finding(
                        "text-style-not-found", Severity.Warning,
                        $"The text names style '{parsed.StyleName}', which is not in the TextStyle table; Windows Installer " +
                        "falls back to DefaultUIFont.",
                        dialog.Name, control.Name));
                }

                var text = new VisibleText(visible, resolved > 0, unresolved > 0);
                if (CheckFit(dialog, control, kind, text, font, out var actualFace) is { } finding)
                {
                    findings.Add(finding);
                }

                if (font.Source != FontSource.Fallback && actualFace is not null)
                {
                    substitutedFaces.TryAdd(font.FaceName, actualFace);
                }
            }
        }

        findings.InsertRange(0, PackageFindings(package, anyText, substitutedFaces));
        return findings;
    }

    private static IEnumerable<Finding> PackageFindings(
        DialogPackage package, bool anyText, IReadOnlyDictionary<string, string> substitutedFaces)
    {
        if (anyText && string.IsNullOrEmpty(package.DefaultUIFont))
        {
            yield return new Finding(
                "default-ui-font-missing", Severity.Warning,
                "The package does not set the DefaultUIFont property, so Windows Installer uses the system font. " +
                $"Set it to a TextStyle row. Text checks use {FontResolver.Fallback.FaceName} {FontResolver.Fallback.Size} pt " +
                "and are approximate.");
        }
        else if (anyText && !package.TextStyles.ContainsKey(package.DefaultUIFont!))
        {
            yield return new Finding(
                "default-ui-font-style-not-found", Severity.Warning,
                $"DefaultUIFont is '{package.DefaultUIFont}', which is not in the TextStyle table. Text checks use " +
                $"{FontResolver.Fallback.FaceName} {FontResolver.Fallback.Size} pt and are approximate.");
        }

        foreach (var (requested, actual) in substitutedFaces)
        {
            yield return new Finding(
                "font-not-installed", Severity.Warning,
                $"The font '{requested}' is not installed on this machine. Windows draws '{actual}' instead, and so does " +
                "msiexec here; text checks use it and are approximate. Machines with the font installed may differ.",
                Font: actual, Approximate: true);
        }
    }

    private static TextKind? Kind(Control control) => control.Type switch
    {
        // A zero-sized control is a deliberate way to have an invisible button or label.
        _ when control.Width <= 0 || control.Height <= 0 => null,
        _ when !control.Has(ControlAttributes.Visible) && !control.ShownByCondition => null,
        _ when control.Has(ControlAttributes.Indirect) => null,
        "Text" => TextKind.Text,
        "CheckBox" when !control.Has(ControlAttributes.PushLike) && !HasImage(control) => TextKind.CheckBox,
        "PushButton" when !control.Has(ControlAttributes.ElevationShield) && !HasImage(control) => TextKind.PushButton,
        _ => null,
    };

    private static bool HasImage(Control control) =>
        control.Has(ControlAttributes.Bitmap) || control.Has(ControlAttributes.Icon);

    // actualFace: the face GDI used, when it is not the one requested.
    private static Finding? CheckFit(
        Dialog dialog, Control control, TextKind kind, VisibleText text, ResolvedFont font, out string? actualFace)
    {
        actualFace = null;
        var noWrap = kind == TextKind.PushButton || (kind == TextKind.Text && control.Has(ControlAttributes.NoWrap));
        var options = noWrap ? MeasureOptions.NoWrap : MeasureOptions.None;
        if (kind == TextKind.Text && control.Has(ControlAttributes.NoPrefix))
        {
            options |= MeasureOptions.NoPrefix;
        }

        var checks = new List<DpiCheck>();
        var raster = false;
        foreach (var dpi in InstallerUnits.CheckedDpis)
        {
            var (insetWidth, insetHeight) = Insets(control, kind, dpi);
            var availableWidth = InstallerUnits.ToPixels(control.Width, dpi) - insetWidth;
            var availableHeight = InstallerUnits.ToPixels(control.Height, dpi) - insetHeight;

            var measured = TextMeasurer.Measure(text.Text, font, dpi, Math.Max(availableWidth, 1), options);
            if (measured.FaceSubstituted)
            {
                actualFace = measured.FaceName;
            }

            raster |= measured.Raster;

            checks.Add(new DpiCheck(dpi, measured.Width, measured.Height, availableWidth, availableHeight,
                measured.Width <= availableWidth && measured.Height <= availableHeight));
        }

        // A check box no wider than its box is a box whose label is a separate Text control; its own text never shows.
        if (checks.All(c => c.Fits) || (kind == TextKind.CheckBox && checks[0].AvailableWidth <= 0))
        {
            return null;
        }

        var failing = checks.Where(c => !c.Fits).ToList();
        var first = failing[0];
        var (suggestedWidth, suggestedHeight) = Suggest(control, kind, text, font, options, checks);
        var usedFace = actualFace ?? font.FaceName;
        // A bitmap font has fixed sizes made for 96 DPI; what Windows picks at other scales is not certain.
        var rasterGuess = raster && failing.All(c => c.Dpi != 96);
        var approximate = font.Source == FontSource.Fallback || actualFace is not null || rasterGuess;

        var (rule, problem) = (kind, noWrap) switch
        {
            (TextKind.PushButton, _) =>
                ("button-text-clipped",
                    $"needs {first.TextWidth} px, the button leaves {first.AvailableWidth} px for text"),
            (TextKind.Text, true) =>
                ("text-truncated",
                    $"needs {first.TextWidth} px on its single line, the control is {first.AvailableWidth} px wide; " +
                    "it is cut off with \"...\""),
            _ when first.TextWidth > first.AvailableWidth =>
                ("text-clipped",
                    $"has a word {first.TextWidth} px wide in a {first.AvailableWidth} px wide text area; it is cut off"),
            // Seen in msiexec (calibration screenshot): a Text control shows the lines that fit and ends the last
            // one with "..."; a check box shows its wrapped lines vertically centred and cut at the top and bottom.
            (TextKind.Text, _) =>
                ("text-clipped",
                    $"wraps to {first.TextHeight / LineHeight(font, first.Dpi)} lines, {first.TextHeight} px, in a " +
                    $"{first.AvailableHeight} px high text area; Windows shows " +
                    $"{Plural(first.AvailableHeight / LineHeight(font, first.Dpi), "line")} ending in \"...\""),
            _ =>
                ("text-clipped",
                    $"wraps to {first.TextHeight / LineHeight(font, first.Dpi)} lines, {first.TextHeight} px, in a " +
                    $"{first.AvailableHeight} px high check box; the lines are cut off at the top and bottom"),
        };

        var message =
            $"At {first.Dpi} DPI ({font.FaceName} {font.Size} pt{(font.Bits.HasFlag(TextStyleBits.Bold) ? " bold" : "")}), " +
            $"'{text.Text}' {problem}. " +
            $"Fails at {string.Join(", ", failing.Select(c => c.Dpi))} DPI." +
            (suggestedWidth is { } w ? $" Smallest width that fits at 100, 125 and 150 %: {w} units (now {control.Width})." : "") +
            (suggestedHeight is { } h ? $" Smallest height that fits at 100, 125 and 150 %: {h} units (now {control.Height})." : "") +
            (text.UsesProperties ? " [Property] references are measured with their values from the Property table." : "") +
            (text.LowerBound ? " Some [Property] references have no value in the package and count as empty, so the real text is at least this long." : "") +
            (rasterGuess ? $" {usedFace} is a bitmap font, so results above 100 % are approximate." :
                approximate ? $" Approximate: measured with {usedFace}." : "");

        return new Finding(
            rule,
            failing.Any(c => c.Dpi == 96) ? Severity.Error : Severity.Warning,
            message,
            dialog.Name,
            control.Name,
            usedFace,
            approximate,
            text.LowerBound,
            suggestedWidth,
            suggestedHeight,
            checks);
    }

    private static (int Width, int Height) Insets(Control control, TextKind kind, int dpi)
    {
        var border = control.Has(ControlAttributes.Sunken) ? 2 * Scale(SunkenBorder, dpi) : 0;
        return kind switch
        {
            TextKind.CheckBox => (Scale(CheckBoxTextOffset, dpi), 0),
            TextKind.PushButton => (Scale(PushButtonTextMargin, dpi), 0),
            _ => (border, border),
        };
    }

    // Wrapping text that is too high keeps its width and gets a taller control. Otherwise the control gets wider:
    // as wide as the widest word for wrapping text, as the whole line for buttons and NoWrap text, and for a check
    // box as its whole label, since the usual fix there is one line in a wider control.
    private static (int? Width, int? Height) Suggest(
        Control control, TextKind kind, VisibleText text, ResolvedFont font, MeasureOptions options, IReadOnlyList<DpiCheck> checks)
    {
        var tooNarrow = checks.Any(c => c.TextWidth > c.AvailableWidth);
        if (kind == TextKind.Text && !options.HasFlag(MeasureOptions.NoWrap) && !tooNarrow)
        {
            return (null, checks.Max(c =>
                MinUnits(c.TextHeight + Insets(control, kind, c.Dpi).Height, c.Dpi)));
        }

        // Wrapping text only needs room for its widest word; single-line text needs the whole line.
        var wraps = kind == TextKind.Text && !options.HasFlag(MeasureOptions.NoWrap);
        var pieces = wraps ? text.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [text.Text];
        var width = InstallerUnits.CheckedDpis.Max(dpi =>
        {
            var widest = pieces.Max(piece =>
                TextMeasurer.Measure(piece, font, dpi, int.MaxValue / 2, options | MeasureOptions.NoWrap).Width);
            return MinUnits(widest + Insets(control, kind, dpi).Width, dpi);
        });

        var height = checks.Any(c => c.TextHeight > c.AvailableHeight && kind == TextKind.PushButton)
            ? checks.Max(c => MinUnits(c.TextHeight, c.Dpi))
            : (int?)null;

        return (width > control.Width ? width : null, height);
    }

    private static int LineHeight(ResolvedFont font, int dpi) =>
        TextMeasurer.Measure("x", font, dpi, 1000, MeasureOptions.None).LineHeight;

    private static string Plural(int count, string noun) => count switch
    {
        0 => $"less than one {noun}",
        1 => $"1 {noun}",
        _ => $"{count} {noun}s",
    };

    private static int Scale(int pixelsAt96, int dpi) =>
        (int)Math.Round(pixelsAt96 * dpi / 96.0, MidpointRounding.AwayFromZero);

    // The smallest number of installer units that gives at least `pixels` at this DPI.
    private static int MinUnits(int pixels, int dpi)
    {
        var units = Math.Max(0, (int)Math.Floor(pixels * 288.0 / (4 * dpi)));
        while (InstallerUnits.ToPixels(units, dpi) < pixels)
        {
            units++;
        }

        return units;
    }
}
