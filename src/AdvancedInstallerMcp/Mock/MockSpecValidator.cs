using System.Text.RegularExpressions;

namespace AdvancedInstallerMcp.Mock;

// ADR 0008: a mock may only contain what Windows Installer can show. Every problem is listed, so one round trip fixes
// them all. Controls outside the dialog are not refused: lint_dialog reports them on the picture.
public static class MockSpecValidator
{
    public static readonly IReadOnlySet<string> ControlTypes = new SortedSet<string>(StringComparer.Ordinal)
    {
        "Billboard", "Bitmap", "CheckBox", "ComboBox", "DirectoryCombo", "DirectoryList", "Edit", "GroupBox",
        "Hyperlink", "Icon", "Line", "ListBox", "ListView", "MaskedEdit", "PathEdit", "ProgressBar", "PushButton",
        "RadioButtonGroup", "ScrollableText", "SelectionTree", "Text", "VolumeCostList", "VolumeSelectCombo",
    };

    public static readonly IReadOnlySet<string> InputTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Edit", "MaskedEdit", "PathEdit", "CheckBox", "RadioButtonGroup", "ComboBox", "ListBox",
    };

    private static readonly HashSet<string> ItemTypes = new(StringComparer.Ordinal) { "RadioButtonGroup", "ComboBox", "ListBox" };

    private static readonly HashSet<string> ConditionActions = new(StringComparer.Ordinal) { "Show", "Hide", "Enable", "Disable", "Default" };

    private static readonly HashSet<string> Frames = new(StringComparer.Ordinal) { "wizard", "none" };

    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_.]*\z", RegexOptions.CultureInvariant);

    // The spec arrives as JSON from an MCP client, so any non-nullable member can be null at runtime. Those become
    // listed problems; nothing here may throw.
    public static IReadOnlyList<string> Problems(MockSpec spec)
    {
        var problems = new List<string>();
        var frameNames = WizardFrame.Load().Controls.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        void CheckName(string what, string? name, int maxLength)
        {
            if (name is null)
            {
                problems.Add($"{what} has a missing name.");
            }
            else if (!Identifier.IsMatch(name) || name.Length > maxLength)
            {
                problems.Add($"{what} '{name}' is not a Windows Installer identifier (letters, digits, '_' and '.', " +
                    $"starting with a letter or '_', at most {maxLength} characters).");
            }
        }

        foreach (var style in spec.TextStyles ?? [])
        {
            if (style is null)
            {
                problems.Add("A text style entry is null.");
                continue;
            }

            CheckName("Text style", style.Name, 72);
        }

        foreach (var name in (spec.Properties ?? []).Keys)
        {
            CheckName("Property", name, 72);
        }

        if (spec.Dialogs is null)
        {
            problems.Add("'dialogs' is missing.");
            return problems;
        }

        var dialogs = new List<MockDialogSpec>();
        foreach (var dialog in spec.Dialogs)
        {
            if (dialog is null)
            {
                problems.Add("A dialog entry is null.");
            }
            else
            {
                dialogs.Add(dialog);
            }
        }

        foreach (var duplicate in dialogs.Where(d => d.Name is not null).GroupBy(d => d.Name).Where(g => g.Count() > 1))
        {
            problems.Add($"Dialog '{duplicate.Key}' appears {duplicate.Count()} times.");
        }

        foreach (var dialog in dialogs)
        {
            var dialogName = dialog.Name ?? "(unnamed dialog)";
            CheckName("Dialog", dialog.Name, 72);

            var frame = dialog.Frame ?? "wizard";
            if (!Frames.Contains(frame))
            {
                problems.Add($"Dialog '{dialogName}': frame '{frame}' is not 'wizard' or 'none'.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            if (dialog.Controls is null)
            {
                problems.Add($"Dialog '{dialogName}': 'controls' is missing.");
            }

            foreach (var control in dialog.Controls ?? [])
            {
                if (control is null)
                {
                    problems.Add($"Dialog '{dialogName}': a control entry is null.");
                    continue;
                }

                var controlName = control.Name ?? "(unnamed control)";
                var where = $"{dialogName}.{controlName}";
                CheckName($"{where}: control", control.Name, 50);

                if (control.Name is not null)
                {
                    if (!names.Add(control.Name))
                    {
                        problems.Add($"{where}: the control name appears twice on the page.");
                    }

                    if (frame == "wizard" && frameNames.Contains(control.Name))
                    {
                        problems.Add($"{where}: '{control.Name}' is a control of the wizard frame; pick another name.");
                    }
                }

                var knownType = control.Type is not null && ControlTypes.Contains(control.Type);
                if (control.Type is null)
                {
                    problems.Add($"{where}: the control has no type. Use one of: {string.Join(", ", ControlTypes)}.");
                }
                else if (!knownType)
                {
                    problems.Add($"{where}: '{control.Type}' is not a Windows Installer control type. Use one of: " +
                        $"{string.Join(", ", ControlTypes)}.");
                }

                var isInput = knownType && InputTypes.Contains(control.Type!);
                if (control.Property is { } property)
                {
                    CheckName($"{where}: property", property, 72);
                    if (isInput && property.Any(char.IsLower))
                    {
                        problems.Add($"{where}: '{property}' has lower-case letters, so it is private and cannot be " +
                            $"set on the msiexec command line. Use upper case ('{property.ToUpperInvariant()}').");
                    }
                }
                else if (isInput)
                {
                    problems.Add($"{where}: a {control.Type} needs the property it sets.");
                }

                var items = new List<MockItem>();
                foreach (var item in control.Items ?? [])
                {
                    if (item is null)
                    {
                        problems.Add($"{where}: an item entry is null.");
                    }
                    else
                    {
                        items.Add(item);
                        if (item.Value is null)
                        {
                            problems.Add($"{where}: an item has no value.");
                        }
                    }
                }

                if (items.Count > 0 && knownType && !ItemTypes.Contains(control.Type!))
                {
                    problems.Add($"{where}: only RadioButtonGroup, ComboBox and ListBox take items.");
                }
                else if (control.Type == "RadioButtonGroup" && items.Count == 0)
                {
                    problems.Add($"{where}: a RadioButtonGroup needs its buttons in 'items'.");
                }
            }

            foreach (var condition in dialog.Conditions ?? [])
            {
                if (condition is null)
                {
                    problems.Add($"Dialog '{dialogName}': a condition entry is null.");
                    continue;
                }

                if (condition.Control is null
                    || (!names.Contains(condition.Control) && !(frame == "wizard" && frameNames.Contains(condition.Control))))
                {
                    problems.Add($"{dialogName}: condition on '{condition.Control}', which is not on the page.");
                }

                if (condition.Action is null || !ConditionActions.Contains(condition.Action))
                {
                    problems.Add($"{dialogName}.{condition.Control}: condition action '{condition.Action}' is not one of " +
                        $"{string.Join(", ", ConditionActions)}.");
                }
            }
        }

        return problems;
    }
}
