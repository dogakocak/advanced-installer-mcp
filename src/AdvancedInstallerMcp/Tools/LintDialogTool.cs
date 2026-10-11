using System.ComponentModel;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Msi;
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
        "size in installer units that fits. Use it after changing dialog texts, fonts or control sizes, instead of " +
        "estimating text width from character counts. Point it at the .msi from the build output, not the .aip.")]
    public static LintReport LintDialog(
        [Description("Full path of the built .msi package.")] string msiPath,
        [Description("Name of one dialog (the Dialog table's key, e.g. 'WelcomeDlg'). Omit to check every dialog.")]
        string? dialog = null)
    {
        if (!File.Exists(msiPath))
        {
            throw new McpException($"File not found: {msiPath}");
        }

        if (string.Equals(Path.GetExtension(msiPath), ".aip", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("lint_dialog reads the built .msi, not the .aip project. Build the project first.");
        }

        using var database = OpenPackage(msiPath);
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

    private static MsiDatabase OpenPackage(string msiPath)
    {
        try
        {
            return MsiDatabase.OpenReadOnly(msiPath);
        }
        catch (MsiException e)
        {
            throw new McpException($"Not a readable MSI package: {msiPath} ({e.Message})");
        }
    }
}
