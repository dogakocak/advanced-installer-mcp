using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Dialogs;

// Opt-in check against a real package (e.g. one built by Advanced Installer): set ADVINST_MCP_REAL_MSI to its path.
// Skipped otherwise, so the suite never depends on files outside the repo.
public sealed class RealPackageTests
{
    [Fact]
    public void Every_dialog_of_a_real_package_loads_and_every_text_resolves_a_font()
    {
        var path = Environment.GetEnvironmentVariable("ADVINST_MCP_REAL_MSI");
        Assert.SkipWhen(string.IsNullOrEmpty(path), "ADVINST_MCP_REAL_MSI is not set.");

        using var database = MsiDatabase.OpenReadOnly(path!);
        var package = DialogPackage.Load(database);

        Assert.SkipWhen(package.DialogNames.Count == 0, "The package has no dialogs.");

        foreach (var name in package.DialogNames)
        {
            var dialog = package.FindDialog(name).ShouldNotBeNull();
            dialog.Controls.ShouldNotBeEmpty($"{name} has no controls");

            foreach (var control in dialog.Controls)
            {
                var text = ControlText.Parse(control.Text, package.Properties);
                var font = FontResolver.Resolve(text.StyleName, package.TextStyles, package.DefaultUIFont);
                font.Issue.ShouldNotBe(FontIssue.TextStyleNotFound, $"{name}.{control.Name}: {control.Text}");
            }
        }
    }
}
