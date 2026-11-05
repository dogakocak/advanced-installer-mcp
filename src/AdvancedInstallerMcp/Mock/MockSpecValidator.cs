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

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Problems(MockSpec spec)
    {
        var problems = new List<string>();
        var frameNames = WizardFrame.Load().Controls.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        void CheckName(string what, string name, int maxLength)
        {
            if (!Identifier.IsMatch(name) || name.Length > maxLength)
            {
                problems.Add($"{what} '{name}' is not a Windows Installer identifier (letters, digits, '_' and '.', " +
                    $"starting with a letter or '_', at most {maxLength} characters).");
            }
        }

        foreach (var style in spec.TextStyles ?? [])
        {
            CheckName("Text style", style.Name, 72);
        }

        foreach (var name in (spec.Properties ?? []).Keys)
        {
            CheckName("Property", name, 72);
        }

        foreach (var duplicate in spec.Dialogs.GroupBy(d => d.Name).Where(g => g.Count() > 1))
        {
            problems.Add($"Dialog '{duplicate.Key}' appears {duplicate.Count()} times.");
        }

        foreach (var dialog in spec.Dialogs)
        {
            CheckName("Dialog", dialog.Name, 72);
            if (!Frames.Contains(dialog.Frame))
            {
                problems.Add($"Dialog '{dialog.Name}': frame '{dialog.Frame}' is not 'wizard' or 'none'.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var control in dialog.Controls)
            {
                var where = $"{dialog.Name}.{control.Name}";
                CheckName("Control", control.Name, 50);

                if (!names.Add(control.Name))
                {
                    problems.Add($"{where}: the control name appears twice on the page.");
                }

                if (dialog.Frame == "wizard" && frameNames.Contains(control.Name))
                {
                    problems.Add($"{where}: '{control.Name}' is a control of the wizard frame; pick another name.");
                }

                if (!ControlTypes.Contains(control.Type))
                {
                    problems.Add($"{where}: '{control.Type}' is not a Windows Installer control type. Use one of: " +
                        $"{string.Join(", ", ControlTypes)}.");
                    continue;
                }

                if (control.Property is { } property)
                {
                    CheckName($"{where}: property", property, 72);
                    if (InputTypes.Contains(control.Type) && property.Any(char.IsLower))
                    {
                        problems.Add($"{where}: '{property}' has lower-case letters, so it is private and cannot be " +
                            $"set on the msiexec command line. Use upper case ('{property.ToUpperInvariant()}').");
                    }
                }
                else if (InputTypes.Contains(control.Type))
                {
                    problems.Add($"{where}: a {control.Type} needs the property it sets.");
                }

                if (control.Items is { Count: > 0 } && !ItemTypes.Contains(control.Type))
                {
                    problems.Add($"{where}: only RadioButtonGroup, ComboBox and ListBox take items.");
                }
                else if (control.Type == "RadioButtonGroup" && control.Items is not { Count: > 0 })
                {
                    problems.Add($"{where}: a RadioButtonGroup needs its buttons in 'items'.");
                }
            }

            foreach (var condition in dialog.Conditions ?? [])
            {
                if (!names.Contains(condition.Control) && !(dialog.Frame == "wizard" && frameNames.Contains(condition.Control)))
                {
                    problems.Add($"{dialog.Name}: condition on '{condition.Control}', which is not on the page.");
                }

                if (!ConditionActions.Contains(condition.Action))
                {
                    problems.Add($"{dialog.Name}.{condition.Control}: condition action '{condition.Action}' is not one of " +
                        $"{string.Join(", ", ConditionActions)}.");
                }
            }
        }

        return problems;
    }
}
