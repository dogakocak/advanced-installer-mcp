using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Mock;
using AdvancedInstallerMcp.Msi;
using AdvancedInstallerMcp.Render;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

[McpServerToolType]
public static class MockDialogTool
{
    // Enums as names (Severity "Warning"), as lint_dialog's result shows them.
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [McpServerTool(Name = "mock_dialog", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Shows proposed setup pages before any project is changed. Takes the pages as Windows Installer table rows " +
        "(Dialog, Control, RadioButton/ComboBox/ListBox items, ControlCondition, Property defaults), adds Advanced " +
        "Installer's wizard frame (banner, lines, Back/Next/Cancel) unless frame is 'none', writes a throw-away MSI and " +
        "draws every page the way render_dialog draws a built one, so only what Windows Installer can show appears. " +
        "Returns a picture per page and lint_dialog's findings (text that does not fit, overlaps, tab order, choices " +
        "without a default, a control that does not suit the number of choices). With outputDir it also writes the " +
        "pictures and index.html, an approval page with each page's inputs, why each control was chosen, and the " +
        "msiexec /qn command line: show it to the user and change the .aip only after they approve. Refuses control " +
        "types Windows Installer does not have and lower-case input properties (they cannot be set unattended).")]
    public static CallToolResult MockDialog(
        [Description("The proposed pages.")] MockSpec spec,
        [Description("Draw only this page. Omit for all.")] string? dialog = null,
        [Description("Display scaling in percent: 100, 125 or 150.")] int scaling = 100,
        [Description("Property values for another state of the pages, as in render_dialog. null or \"\" unsets.")]
        Dictionary<string, string?>? properties = null,
        [Description("Folder for <Dialog>.png and the approval page index.html. Omit to only return the pictures.")]
        string? outputDir = null)
    {
        var dpi = DisplayScaling.ToDpi(scaling);

        var problems = MockSpecValidator.Problems(spec);
        if (problems.Count > 0)
        {
            throw new McpException("The spec has problems:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "- " + p)));
        }

        var names = spec.Dialogs.Select(d => d.Name).ToList();
        if (dialog is not null && !names.Contains(dialog, StringComparer.Ordinal))
        {
            throw new McpException($"The spec has no page '{dialog}'. Pages: {string.Join(", ", names)}.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"advinst-mock-{Guid.NewGuid():N}.msi");
        try
        {
            PackageWriter.Write(path, MockPackageBuilder.Build(spec));
            using var database = MsiDatabase.OpenReadOnly(path);
            var package = DialogPackage.Load(database);

            var shown = (dialog is null ? names : [dialog]).Select(n => package.FindDialog(n)!).ToList();
            var findings = DialogLinter.Lint(package, shown);
            var rendered = shown.Select(d => DialogRenderer.Render(package, d, dpi, properties ?? [])).ToList();

            string? approvalPage = null;
            if (outputDir is not null)
            {
                var dir = Path.GetFullPath(outputDir);
                Directory.CreateDirectory(dir);
                for (var i = 0; i < shown.Count; i++)
                {
                    File.WriteAllBytes(Path.Combine(dir, shown[i].Name + ".png"), rendered[i].Png);
                }

                approvalPage = Path.Combine(dir, "index.html");
                File.WriteAllText(approvalPage, ApprovalPage.Html(spec with { Dialogs = spec.Dialogs.Where(d => shown.Any(s => s.Name == d.Name)).ToList() }, findings));
            }

            var report = JsonSerializer.Serialize(new
            {
                dialogs = rendered.Select(r => r.Report),
                findings,
                approvalPage,
            }, ReportJson);

            return new CallToolResult
            {
                Content =
                [
                    .. rendered.Select(r => (ContentBlock)ImageContentBlock.FromBytes(r.Png, "image/png")),
                    new TextContentBlock { Text = report },
                ],
            };
        }
        finally
        {
            File.Delete(path);
        }
    }
}
