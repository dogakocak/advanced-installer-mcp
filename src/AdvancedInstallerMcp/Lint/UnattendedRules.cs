using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Lint;

// ADR 0009: inputs on a page must reach the install and be settable without the page (msiexec /qn PROP=value).
public static class UnattendedRules
{
    private static readonly HashSet<string> InputTypes = new(StringComparer.Ordinal)
    {
        "Edit", "MaskedEdit", "PathEdit", "CheckBox", "RadioButtonGroup", "ComboBox", "ListBox",
    };

    // ComboBox: msidbControlAttributesComboList, a drop-down the user cannot type into.
    private const int ComboList = 0x00020000;

    private const int MaxRadioButtons = 5;
    private const int MaxComboListItemsForRadios = 3;

    public static bool IsInput(Control control) => InputTypes.Contains(control.Type) && control.Property is not null;

    public static IEnumerable<Finding> Check(DialogPackage package, Dialog dialog)
    {
        var secure = ListProperty(package, "SecureCustomProperties");
        var hidden = ListProperty(package, "MsiHiddenProperties");

        foreach (var control in dialog.Controls.Where(IsInput).OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            var property = control.Property!;
            var isPrivate = property.Any(char.IsLower);
            var used = package.InstallUsage.IsUsed(property);

            if (used && isPrivate)
            {
                yield return new Finding("input-property-private", Severity.Error,
                    $"'{property}' has lower-case letters, so it is private: msiexec ignores it on the command line and " +
                    "Windows Installer does not pass it from the dialogs to the install, which uses it. Rename it in upper " +
                    $"case ('{property.ToUpperInvariant()}') everywhere it is used.",
                    dialog.Name, control.Name);
            }
            else if (used && !secure.Contains(property) && !package.InstallUsage.IsDirectory(property))
            {
                yield return new Finding("input-property-not-secure", Severity.Error,
                    $"The install uses '{property}', but it is not in SecureCustomProperties: when the install runs " +
                    "elevated, the value entered on this page is dropped and the install sees the default. Add it to " +
                    "SecureCustomProperties.",
                    dialog.Name, control.Name);
            }

            if (control.Type == "Edit" && control.Has(ControlAttributes.Password) && !hidden.Contains(property))
            {
                yield return new Finding("password-not-hidden", Severity.Warning,
                    $"'{property}' is a password field but not in MsiHiddenProperties, so a verbose log shows the value in " +
                    "clear text. Add it to MsiHiddenProperties.",
                    dialog.Name, control.Name);
            }

            var choices = control.Type switch
            {
                "RadioButtonGroup" => package.RadioButtons(property).Count,
                "ComboBox" => package.ComboItems(property).Count,
                _ => (int?)null,
            };

            if (choices is null)
            {
                continue;
            }

            if (!package.Properties.ContainsKey(property))
            {
                var radios = control.Type == "RadioButtonGroup";
                yield return new Finding("choice-without-default", radios ? Severity.Warning : Severity.Info,
                    (radios ? "No button is selected when the page opens" : "The list opens empty") +
                    $", and an unattended install without {property}=... passes an empty value that is none of the " +
                    $"choices. Add a Property row with the default value.",
                    dialog.Name, control.Name);
            }

            if (control.Type == "RadioButtonGroup" && choices > MaxRadioButtons)
            {
                yield return new Finding("control-choice", Severity.Info,
                    $"{choices} radio buttons: a drop-down list (ComboBox with the ComboList attribute) is easier to " +
                    "scan from 5 choices on.",
                    dialog.Name, control.Name);
            }
            else if (control.Type == "ComboBox" && control.Has(ComboList) && choices <= MaxComboListItemsForRadios)
            {
                yield return new Finding("control-choice", Severity.Info,
                    $"A drop-down list with {choices} entries hides choices that radio buttons would show at once.",
                    dialog.Name, control.Name);
            }
        }
    }

    private static HashSet<string> ListProperty(DialogPackage package, string name) =>
        (package.Properties.GetValueOrDefault(name) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
