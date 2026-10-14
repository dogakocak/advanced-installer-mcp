using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Render;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

[McpServerToolType]
public static class RenderDialogTool
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "render_dialog", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Draws one setup dialog of a built MSI package as a PNG, without installing anything, the way msiexec shows it " +
        "when the dialog opens: the package's fonts and text, Windows-styled buttons, check boxes, radio buttons and " +
        "edit boxes, pictures from the Binary table. Text is laid out exactly as lint_dialog measures it, so a label cut " +
        "off in the picture is one lint_dialog reports. ControlCondition rows are evaluated against the Property table; " +
        "pass 'properties' to set values a custom action or the user would set (for example to see the other state of a " +
        "page). Controls whose content only exists during an install (feature trees, folder and disk lists, progress " +
        "bars, billboards) are drawn as labelled frames. Returns the image and a JSON report: fonts used, placeholders, " +
        "each condition's result (null = not evaluated), [Property] references the package cannot resolve (drawn as " +
        "written), and pictures that could not be drawn. Use it to look at a dialog after changing it.")]
    public static CallToolResult RenderDialog(
        [Description("Full path of the built .msi package.")] string msiPath,
        [Description("Name of the dialog (the Dialog table's key, e.g. 'WelcomeDlg').")] string dialog,
        [Description("Display scaling in percent: 100, 125 or 150.")] int scaling = 100,
        [Description(
            "Property values to use instead of the package's, e.g. {\"AI_REPLACE_PRODUCTS\": \"1\"}. " +
            "null or \"\" unsets a property, as an empty value does in Windows Installer.")]
        Dictionary<string, string?>? properties = null,
        [Description("Also save the PNG to this path (.png). Omit to only return it.")] string? outputPath = null)
    {
        var dpi = DisplayScaling.ToDpi(scaling);

        if (outputPath is not null && !string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("outputPath must end in .png.");
        }

        using var database = PackageFile.Open(msiPath, "render_dialog");
        var package = DialogPackage.Load(database);
        var found = package.FindDialog(dialog) ?? throw new McpException(
            $"The package has no dialog '{dialog}'. Dialogs: {string.Join(", ", package.DialogNames)}.");

        var rendered = DialogRenderer.Render(package, found, dpi, properties ?? []);

        string? savedTo = null;
        if (outputPath is not null)
        {
            savedTo = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(savedTo)!);
            File.WriteAllBytes(savedTo, rendered.Png);
        }

        var report = JsonSerializer.SerializeToNode(rendered.Report, ReportJson)!.AsObject();
        if (savedTo is not null)
        {
            report["savedTo"] = savedTo;
        }

        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(rendered.Png, "image/png"),
                new TextContentBlock { Text = report.ToJsonString() },
            ],
        };
    }
}
