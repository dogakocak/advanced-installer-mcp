using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Lint;

// What a Text control paints: its text as shown and the pixels it covers from the control's top-left corner.
internal sealed record TextInk(string Text, int Width, int Height);

// lint_dialog's layout checks (ADR 0005): bounds, overlaps and the tab chain. Bounds and overlaps compare what each
// control paints, in pixels at 100, 125 and 150 %: a Text control only covers the area its text takes up, a combo box
// only its closed box, a radio button group only its buttons.
public static class LayoutRules
{
    // Drawn behind or around other controls on purpose: a banner bitmap under the title, controls inside a group box.
    private static readonly HashSet<string> BackgroundTypes = new(StringComparer.Ordinal)
    {
        "Bitmap", "GroupBox", "Line", "Billboard",
    };

    // Height covers the dropped-down list; closed, the box is one text line high.
    private static readonly HashSet<string> ComboTypes = new(StringComparer.Ordinal)
    {
        "ComboBox", "DirectoryCombo", "VolumeSelectCombo",
    };

    // Controls the user operates to get through the dialog. Lists that only show information (FilesInUse,
    // VolumeCostList, ListBox) and ScrollableText are left out of many standard templates' tab cycles on purpose.
    private static readonly HashSet<string> FocusableTypes = new(StringComparer.Ordinal)
    {
        "CheckBox", "ComboBox", "DirectoryCombo", "DirectoryList", "Edit", "Hyperlink", "ListView", "MaskedEdit",
        "PathEdit", "PushButton", "RadioButtonGroup", "SelectionTree", "VolumeSelectCombo",
    };

    public static bool IsComboType(string type) => ComboTypes.Contains(type);

    // A closed combo box: one line of the dialog font plus border and padding. Approximate, not calibrated.
    public const int ComboBoxPadding = 8;

    // Pixels of overlap (at 96 DPI, scaled) ignored when one side is text: the empty top and bottom of a line cell.
    public const int TextLineSlack = 2;

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public bool IsEmpty => Right <= Left || Bottom <= Top;

        public Box Intersect(Box other) => new(
            Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    }

    public static IEnumerable<Finding> Check(DialogPackage package, Dialog dialog)
    {
        var shown = dialog.Controls.Where(CanBeSeen).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        var painted = shown.ToDictionary(
            c => c.Name,
            c => InstallerUnits.CheckedDpis.ToDictionary(dpi => dpi, dpi => Painted(package, c, dpi)),
            StringComparer.Ordinal);

        return Bounds(dialog, shown, painted)
            .Concat(Overlaps(package, dialog, shown, painted))
            .Concat(dialog.IsErrorDialog ? [] : TabChain(dialog));
    }

    // Same rule as the text check: a zero-sized control is a deliberate way to hide it, and a control without the
    // Visible attribute only shows when a condition shows it.
    public static bool CanBeSeen(Control control) =>
        control.Width > 0 && control.Height > 0 &&
        (control.Has(ControlAttributes.Visible) || control.ShownByCondition);

    // The parts of a control that paint something, in dialog pixels at this DPI.
    private static IReadOnlyList<Box> Painted(DialogPackage package, Control control, int dpi)
    {
        var full = ToBox(control.X, control.Y, control.Width, control.Height, dpi);

        if (control.Type == "Text" && !control.Has(ControlAttributes.Sunken))
        {
            // Unknown text (set by an event at run time, or Indirect) is assumed to fill the control.
            if (control.TextFromEvent || control.Has(ControlAttributes.Indirect))
            {
                return [full];
            }

            if (DialogLinter.MeasureTextInk(package, control, dpi) is not { } ink)
            {
                return [];
            }

            var left = control.Has(ControlAttributes.RightAligned) ? full.Right - ink.Width : full.Left;
            return [new Box(left, full.Top, left + ink.Width, full.Top + ink.Height)];
        }

        if (ComboTypes.Contains(control.Type))
        {
            var font = FontResolver.Resolve(null, package.TextStyles, package.DefaultUIFont);
            var closed = DialogLinter.LineHeight(font, dpi) + DialogLinter.Scale(ComboBoxPadding, dpi);
            return [full with { Bottom = Math.Min(full.Bottom, full.Top + closed) }];
        }

        if (control.Type == "RadioButtonGroup")
        {
            var buttons = control.Property is null ? [] : package.RadioButtons(control.Property);
            return buttons
                .Select(b => ToBox(control.X + b.X, control.Y + b.Y, b.Width, b.Height, dpi))
                .Where(b => !b.IsEmpty)
                .ToList();
        }

        return [full];
    }

    private static Box ToBox(int x, int y, int width, int height, int dpi) => new(
        InstallerUnits.ToPixels(x, dpi), InstallerUnits.ToPixels(y, dpi),
        InstallerUnits.ToPixels(x + width, dpi), InstallerUnits.ToPixels(y + height, dpi));

    private static IEnumerable<Finding> Bounds(
        Dialog dialog, IReadOnlyList<Control> shown, Dictionary<string, Dictionary<int, IReadOnlyList<Box>>> painted)
    {
        // A background bitmap or a separator line drawn wider than the dialog only loses an edge nobody misses.
        foreach (var control in shown.Where(c => c.Type is not ("Bitmap" or "Line")))
        {
            var right = control.X + control.Width;
            var bottom = control.Y + control.Height;
            var where = $"'{control.Name}' ({control.Type}) spans {control.X}..{right} x {control.Y}..{bottom}, " +
                $"the dialog is {dialog.Width} x {dialog.Height} units";

            if (control.X >= dialog.Width || control.Y >= dialog.Height || right <= 0 || bottom <= 0)
            {
                yield return new Finding("control-off-dialog", Severity.Warning,
                    $"{where}; the whole control lies outside and can never be seen. Remove it or hide it with a " +
                    "condition if that is not deliberate.",
                    dialog.Name, control.Name);
                continue;
            }

            var failing = InstallerUnits.CheckedDpis
                .Where(dpi =>
                {
                    var client = ToBox(0, 0, dialog.Width, dialog.Height, dpi);
                    return painted[control.Name][dpi].Any(b => b.Intersect(client) != b);
                })
                .ToList();
            if (failing.Count == 0)
            {
                continue;
            }

            yield return new Finding("control-out-of-bounds",
                failing.Contains(96) ? Severity.Error : Severity.Warning,
                $"{where}; what it shows reaches past the edge and is cut off" +
                $"{(failing.Contains(96) ? "" : $" at {string.Join(", ", failing)} DPI")}. {Fix(control, dialog)}",
                dialog.Name, control.Name);
        }
    }

    private static string Fix(Control control, Dialog dialog)
    {
        var parts = new List<string>();
        if (control.X + control.Width > dialog.Width)
        {
            parts.Add($"X + Width at most {dialog.Width}");
        }

        if (control.Y + control.Height > dialog.Height)
        {
            parts.Add($"Y + Height at most {dialog.Height}");
        }

        if (control.X < 0 || control.Y < 0)
        {
            parts.Add("X and Y not negative");
        }

        return $"Keep {string.Join(" and ", parts)}.";
    }

    private static IEnumerable<Finding> Overlaps(
        DialogPackage package, Dialog dialog, IReadOnlyList<Control> shown,
        Dictionary<string, Dictionary<int, IReadOnlyList<Box>>> painted)
    {
        var candidates = shown.Where(c => !BackgroundTypes.Contains(c.Type)).ToList();

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var (a, b) = (candidates[i], candidates[j]);
                if (Exempt(package, dialog, a, b))
                {
                    continue;
                }

                // A line of text is as high as the font's cell, with empty room above and below the letters, so
                // a touch of a pixel or two involving text shows nothing.
                var text = a.Type == "Text" || b.Type == "Text";
                var overlaps = InstallerUnits.CheckedDpis
                    .Select(dpi => (Dpi: dpi, Area: LargestOverlap(painted[a.Name][dpi], painted[b.Name][dpi]),
                        Slack: text ? DialogLinter.Scale(TextLineSlack, dpi) : 0))
                    .Where(o => o.Area is { } box && box.Right - box.Left > o.Slack && box.Bottom - box.Top > o.Slack)
                    .ToList();
                if (overlaps.Count == 0)
                {
                    continue;
                }

                var (dpi, area, _) = overlaps[0];
                var atAll = overlaps.Count == InstallerUnits.CheckedDpis.Count;
                var message = $"'{a.Name}' ({a.Type}, {Rect(a)}) and '{b.Name}' ({b.Type}, {Rect(b)}) overlap: at " +
                    $"{dpi} DPI what they show shares {area!.Value.Right - area.Value.Left} x " +
                    $"{area.Value.Bottom - area.Value.Top} px" +
                    (atAll ? "" : $" (at {string.Join(", ", overlaps.Select(o => o.Dpi))} DPI only)");
                var conditioned = a.Conditional ? a : b.Conditional ? b : null;
                var unknown = new[] { a, b }.FirstOrDefault(c => c.Type == "Text" && c.TextFromEvent);
                var severity = conditioned is null && unknown is null && dpi == 96 ? Severity.Error : Severity.Warning;

                yield return new Finding("controls-overlap", severity,
                    (conditioned is null
                        ? $"{message}; one covers part of the other. Move or resize one of them."
                        : $"{message}, whenever a ControlCondition shows '{conditioned.Name}'. Move or resize one of " +
                          "them unless that condition never holds while the other is shown.") +
                    (unknown is null
                        ? ""
                        : $" The text of '{unknown.Name}' is set at run time, so it is assumed to fill its control."),
                    dialog.Name, b.Name);
            }
        }
    }

    private static bool Exempt(DialogPackage package, Dialog dialog, Control a, Control b)
    {
        // Two conditioned controls in one place are usually swapped by complementary conditions.
        if (a.Conditional && b.Conditional)
        {
            return true;
        }

        // An error dialog stacks every possible button (OK, Cancel, Retry, ...); Windows Installer shows only the ones
        // the message needs.
        if (dialog.IsErrorDialog && a.Type == "PushButton" && b.Type == "PushButton")
        {
            return true;
        }

        // The same text twice, a pixel apart, is an embossed or shadowed label.
        return a.Type == "Text" && b.Type == "Text" &&
            DialogLinter.MeasureTextInk(package, a, 96) is { } inkA &&
            DialogLinter.MeasureTextInk(package, b, 96) is { } inkB &&
            inkA.Text == inkB.Text;
    }

    private static Box? LargestOverlap(IReadOnlyList<Box> a, IReadOnlyList<Box> b)
    {
        Box? largest = null;
        foreach (var boxA in a)
        {
            foreach (var boxB in b)
            {
                var area = boxA.Intersect(boxB);
                if (!area.IsEmpty && (largest is null || Size(area) > Size(largest.Value)))
                {
                    largest = area;
                }
            }
        }

        return largest;

        static int Size(Box box) => (box.Right - box.Left) * (box.Bottom - box.Top);
    }

    private static string Rect(Control c) => $"x {c.X}, y {c.Y}, {c.Width} x {c.Height} units";

    private static IEnumerable<Finding> TabChain(Dialog dialog)
    {
        var byName = dialog.Controls.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var (column, name) in new[]
                 {
                     ("Control_First", (string?)dialog.FirstControl),
                     ("Control_Default", dialog.DefaultControl),
                     ("Control_Cancel", dialog.CancelControl),
                 })
        {
            if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
            {
                yield return new Finding("dialog-control-missing", Severity.Error,
                    $"The dialog's {column} is '{name}', which is not a control of this dialog.",
                    dialog.Name);
            }
        }

        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var control in dialog.Controls.Where(c => c.Next is not null))
        {
            if (byName.ContainsKey(control.Next!))
            {
                next[control.Name] = control.Next!;
            }
            else
            {
                yield return new Finding("tab-next-missing", Severity.Error,
                    $"Control_Next of '{control.Name}' is '{control.Next}', which is not a control of this dialog; " +
                    "Tab stops there.",
                    dialog.Name, control.Name);
            }
        }

        var reached = next.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var name in reached.Where(n => byName[n].Next is null).Order(StringComparer.Ordinal))
        {
            var from = string.Join("', '", next.Where(p => p.Value == name).Select(p => p.Key).Order(StringComparer.Ordinal));
            yield return new Finding("tab-chain-open", Severity.Error,
                $"'{from}' passes the focus to '{name}', which has no Control_Next, so Tab stops there. The links must " +
                "form a closed cycle: set its Control_Next to the next control in tab order.",
                dialog.Name, name);
        }

        var cycles = Cycles(next);
        var onCycle = cycles.SelectMany(c => c).ToHashSet(StringComparer.Ordinal);

        bool TakesFocus(Control c) =>
            FocusableTypes.Contains(c.Type) && CanBeSeen(c) &&
            (c.Has(ControlAttributes.Enabled) || c.EnabledByCondition);

        foreach (var control in dialog.Controls.Where(TakesFocus).OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!onCycle.Contains(control.Name) && control.Name != dialog.FirstControl)
            {
                yield return new Finding("not-in-tab-order", Severity.Warning,
                    $"'{control.Name}' ({control.Type}) takes the focus but is not on the tab cycle, so the keyboard " +
                    "cannot reach it. Link it into the Control_Next cycle.",
                    dialog.Name, control.Name);
            }
        }

        var focusCycles = cycles.Where(cycle => cycle.Any(n => TakesFocus(byName[n]))).ToList();
        if (focusCycles.Count > 1)
        {
            var groups = string.Join("; ", focusCycles.Select(cycle => string.Join(" > ", cycle)));
            yield return new Finding("tab-chain-split", Severity.Warning,
                $"The tab order is split into {focusCycles.Count} separate cycles ({groups}); Tab never moves from one " +
                "to another. Join them into one cycle.",
                dialog.Name);
        }
    }

    // Every cycle of the Control_Next links, each listed in tab order from the control where the walk entered it.
    // Every control has at most one Control_Next, so each walk ends in a cycle, a dead end or a control seen before.
    private static List<List<string>> Cycles(IReadOnlyDictionary<string, string> next)
    {
        var cycles = new List<List<string>>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in next.Keys.Order(StringComparer.Ordinal))
        {
            var path = new List<string>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = start;
            while (current is not null && !done.Contains(current) && !index.ContainsKey(current))
            {
                index[current] = path.Count;
                path.Add(current);
                current = next.GetValueOrDefault(current);
            }

            if (current is not null && index.TryGetValue(current, out var from))
            {
                cycles.Add(path[from..]);
            }

            done.UnionWith(path);
        }

        return cycles;
    }
}
