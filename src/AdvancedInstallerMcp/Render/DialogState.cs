using AdvancedInstallerMcp.Conditions;
using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Render;

public sealed record ControlState(Control Control, bool Visible, bool Enabled);

// One ControlCondition row and what it evaluated to; null when it needs install-time state or does not parse.
public sealed record ConditionOutcome(string Control, string Action, string Condition, bool? Result);

// How the dialog looks when it opens (ADR 0006): the Control table's attributes, then every ControlCondition row
// whose condition is true, in table order. Properties are the package's, with the caller's values on top
// (a null or empty caller value unsets the property).
public sealed record DialogState(
    IReadOnlyList<ControlState> Controls,
    string? DefaultControl,
    IReadOnlyList<ConditionOutcome> Conditions,
    IReadOnlyDictionary<string, string> Properties)
{
    public static DialogState Evaluate(
        DialogPackage package, Dialog dialog, IReadOnlyDictionary<string, string?> callerProperties)
    {
        var properties = new Dictionary<string, string>(package.Properties, StringComparer.Ordinal);
        foreach (var (name, value) in callerProperties)
        {
            // Windows Installer deletes a property set to an empty value; null is how a JSON caller says the same.
            if (string.IsNullOrEmpty(value))
            {
                properties.Remove(name);
            }
            else
            {
                properties[name] = value;
            }
        }

        return Evaluate(package, dialog, properties, condition => ConditionEvaluator.Evaluate(condition, properties));
    }

    // The same, with conditions answered by someone else (drive_dialog: Windows Installer's engine, ADR 0007) and
    // the property values that engine holds.
    public static DialogState Evaluate(
        DialogPackage package, Dialog dialog, IReadOnlyDictionary<string, string> properties, Func<string, bool?> evaluate)
    {
        var visible = dialog.Controls.ToDictionary(c => c.Name, c => c.Has(ControlAttributes.Visible), StringComparer.Ordinal);
        var enabled = dialog.Controls.ToDictionary(c => c.Name, c => c.Has(ControlAttributes.Enabled), StringComparer.Ordinal);
        var defaultControl = dialog.DefaultControl;
        var outcomes = new List<ConditionOutcome>();

        foreach (var row in package.ControlConditions(dialog.Name))
        {
            var result = evaluate(row.Condition);
            outcomes.Add(new ConditionOutcome(row.Control, row.Action, row.Condition, result));
            if (result != true || !visible.ContainsKey(row.Control))
            {
                continue;
            }

            switch (row.Action)
            {
                case "Show":
                    visible[row.Control] = true;
                    break;
                case "Hide":
                    visible[row.Control] = false;
                    break;
                case "Enable":
                    enabled[row.Control] = true;
                    break;
                case "Disable":
                    enabled[row.Control] = false;
                    break;
                case "Default":
                    defaultControl = row.Control;
                    break;
            }
        }

        return new DialogState(
            dialog.Controls.Select(c => new ControlState(c, visible[c.Name], enabled[c.Name])).ToList(),
            defaultControl,
            outcomes,
            properties);
    }
}
