using System.ComponentModel;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

[McpServerToolType]
public static class LintDialogTool
{
    [McpServerTool(Name = "lint_dialog", ReadOnly = true, Idempotent = true)]
    [Description(
        "Checks the setup dialogs of a built MSI package without installing it. For every Text, CheckBox and PushButton " +
        "control it measures the label with the package's own font (TextStyle / DefaultUIFont) the way Windows draws " +
        "it, at 100, 125 and 150 % display scaling, and reports labels that are cut off, with the smallest control " +
        "size in installer units that fits. It also reports controls that reach past the dialog's edge, controls whose " +
        "visible content overlaps, and a broken tab order (Control_Next links that do not form a closed cycle, controls " +
        "the keyboard cannot reach). Use it after changing dialog texts, fonts, control sizes, positions or tab order, " +
        "instead of estimating text width from character counts. Point it at the .msi from the build output, not the .aip.")]
    public static LintReport LintDialog(
        [Description("Full path of the built .msi package.")] string msiPath,
        [Description("Name of one dialog (the Dialog table's key, e.g. 'WelcomeDlg'). Omit to check every dialog.")]
        string? dialog = null)
    {
        using var database = PackageFile.Open(msiPath, "lint_dialog");
        var package = DialogPackage.Load(database);

        IReadOnlyList<string> names;
        if (dialog is null)
        {
            names = package.DialogNames;
        }
        else if (package.DialogNames.Contains(dialog, StringComparer.Ordinal))
        {
            names = [dialog];
        }
        else
        {
            throw new McpException(
                $"The package has no dialog '{dialog}'. Dialogs: {string.Join(", ", package.DialogNames)}.");
        }

        var dialogs = names.Select(name => package.FindDialog(name)!).ToList();
        return new LintReport(msiPath, names, DialogLinter.Lint(package, dialogs));
    }
}
