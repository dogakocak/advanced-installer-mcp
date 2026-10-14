using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Drive;
using AdvancedInstallerMcp.Msi;
using AdvancedInstallerMcp.Render;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

public sealed record DriveStepInput(
    [property: Description(
        "Values the user enters on the page: {\"PROPERTY\": \"value\"}. An unchecked check box is \"\", as " +
        "Windows Installer leaves its property empty.")]
    Dictionary<string, string?>? Set = null,
    [property: Description("Name of the control the user clicks (the Control table's key on the current page).")]
    string? Click = null);

[McpServerToolType]
public static class DriveDialogTool
{
    // Windows Installer allows one install session per process (ADR 0007).
    private static readonly Lock OneSession = new();

    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "drive_dialog", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Plays the setup wizard of a built MSI package without installing anything, in a restricted Windows " +
        "Installer session: runs InstallUISequence up to the first page, then the given steps — 'set' property " +
        "values the user enters, 'click' a control, which plays its ControlEvent rows in order with Windows " +
        "Installer evaluating every condition. Property-setting custom actions and built-in actions run; custom " +
        "actions that run package code (DLL, EXE, script, PowerShell) are refused by the engine, reported as not run, " +
        "and their outcome is unknown unless 'actionResults' says which properties they would set. Returns a JSON " +
        "report per step (property changes, events played or skipped, dialogs opened, controls that became " +
        "enabled/disabled/shown/hidden) and the final page as a PNG drawn from the same state. Use it to check what a " +
        "page does: is Next enabled after a test, what an unchecked box sets, which page a button opens.")]
    public static CallToolResult DriveDialog(
        [Description("Full path of the built .msi package.")] string msiPath,
        [Description("Steps in order, each either {\"set\": {...}} or {\"click\": \"Control\"}.")] DriveStepInput[] steps,
        [Description("Page to start on. Omit to start on the first page the wizard opens with.")] string? dialog = null,
        [Description("The product is installed: plays the Change/Repair/Remove wizard (Installed = 1).")] bool maintenance = false,
        [Description("Properties as on the msiexec command line, applied before the wizard opens.")]
        Dictionary<string, string?>? properties = null,
        [Description(
            "For a custom action the restricted engine refuses (one that runs package code), the properties it would " +
            "set, e.g. {\"TestSqlConnection\": {\"SQL_OK\": \"1\"}}.")]
        Dictionary<string, Dictionary<string, string?>>? actionResults = null,
        [Description("Display scaling of the picture in percent: 100, 125 or 150.")] int scaling = 100,
        [Description("Draw the final page. false returns only the report.")] bool render = true)
    {
        var dpi = DisplayScaling.ToDpi(scaling);
        var request = new DriveRequest(
            steps.Select(ToStep).ToList(),
            dialog,
            maintenance,
            properties,
            actionResults?.ToDictionary(a => a.Key, a => (IReadOnlyDictionary<string, string?>)a.Value));

        using var database = PackageFile.Open(msiPath, "drive_dialog");
        var package = DialogPackage.Load(database);

        DriveResult result;
        lock (OneSession)
        {
            try
            {
                result = DialogDriver.Drive(msiPath, request);
            }
            catch (DriveException e)
            {
                throw new McpException(e.Message);
            }
            catch (MsiException e)
            {
                throw new McpException(
                    $"Windows Installer cannot play this package ({e.Message}). drive_dialog needs a built install " +
                    "package (summary information and a ProductCode), not a merge module or a bare database.");
            }
        }

        var report = JsonSerializer.SerializeToNode(new
        {
            result.Opening,
            result.Steps,
            result.Dialog,
            result.Ended,
            Controls = result.State.Controls.Select(c => new { c.Control.Name, c.Control.Type, c.Visible, c.Enabled }),
        }, ReportJson)!.AsObject();

        if (!render)
        {
            return new CallToolResult { Content = [new TextContentBlock { Text = report.ToJsonString() }] };
        }

        var page = package.FindDialog(result.Dialog)!;
        var picture = DialogRenderer.Render(package, page, dpi, result.State);
        report["picture"] = JsonSerializer.SerializeToNode(new
        {
            picture.Report.Width,
            picture.Report.Height,
            picture.Report.Dpi,
            picture.Report.Fonts,
            picture.Report.Placeholders,
            picture.Report.UnresolvedProperties,
            picture.Report.Problems,
        }, ReportJson);

        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(picture.Png, "image/png"),
                new TextContentBlock { Text = report.ToJsonString() },
            ],
        };
    }

    private static DriveStep ToStep(DriveStepInput input, int index) =>
        (input.Set, input.Click) switch
        {
            ({ } values, null) => new SetStep(values),
            (null, { } control) => new ClickStep(control),
            _ => throw new McpException($"step {index + 1}: give either 'set' or 'click', not both or neither."),
        };
}
