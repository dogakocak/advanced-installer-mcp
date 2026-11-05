// One-off: reads the frame of a standard Advanced Installer wizard page (everything but the page body) from a
// package Advanced Installer built from a new project, and writes WizardFrame.json for mock_dialog (ADR 0008).
// dotnet run tools/frame/capture-wizard-frame.cs -- <package.msi> <dialog> <out.json>
// With only <package.msi>, lists the package's dialogs.
#:project ../../src/AdvancedInstallerMcp/AdvancedInstallerMcp.csproj
#:property TargetFramework=net10.0-windows
#:property PublishAot=false

using System.Text.Json;
using System.Text.RegularExpressions;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Msi;

using var database = MsiDatabase.OpenReadOnly(args[0]);
var package = DialogPackage.Load(database);
if (args.Length == 1)
{
    Console.WriteLine(string.Join(", ", package.DialogNames));
    return;
}

var (dialogName, output) = (args[1], args[2]);
var dialog = package.FindDialog(dialogName) ?? throw new InvalidOperationException($"No dialog {dialogName}");

Console.WriteLine($"{dialog.Name} {dialog.Width}x{dialog.Height} first={dialog.FirstControl} default={dialog.DefaultControl} cancel={dialog.CancelControl} title={dialog.Title}");
foreach (var c in dialog.Controls.OrderBy(c => c.Y).ThenBy(c => c.X))
{
    Console.WriteLine($"  {c.Name,-24} {c.Type,-12} {c.X},{c.Y} {c.Width}x{c.Height} attr={c.Attributes} prop={c.Property} next={c.Next} text={c.Text}");
}

// Frame = everything down to the banner line and everything reaching past the bottom line (the "Advanced Installer"
// label sits across it); the rest is the page body.
var lines = dialog.Controls.Where(c => c.Type == "Line").OrderBy(c => c.Y).ToList();
var (bannerLine, bottomLine) = (lines.First(), lines.Last());
var frame = dialog.Controls.Where(c => c.Y <= bannerLine.Y || c.Y + c.Height >= bottomLine.Y).ToList();

// Advanced Installer builds frame texts from properties ([DlgTitleFont], [ButtonText_Next], [BannerBitmap]); copy the
// ones the frame names, one level deep, and the text styles they and DefaultUIFont name.
var reference = new Regex(@"\[([A-Za-z_][A-Za-z0-9_.]*)\]");
var styleName = new Regex(@"\{[\\&]([A-Za-z_][A-Za-z0-9_.]*)\}");
var texts = frame.Select(c => c.Text).Append(dialog.Title).ToList();
var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var name in texts.SelectMany(t => reference.Matches(t).Select(m => m.Groups[1].Value)).Append("DefaultUIFont"))
{
    if (package.Properties.TryGetValue(name, out var value))
    {
        properties[name] = value;
    }
}

var styles = texts.Concat(properties.Values).SelectMany(t => styleName.Matches(t).Select(m => m.Groups[1].Value))
    .Append(package.DefaultUIFont ?? "")
    .ToHashSet(StringComparer.Ordinal);

var json = new
{
    source = $"Advanced Installer, new project (default type), dialog {dialogName}",
    width = dialog.Width,
    height = dialog.Height,
    title = dialog.Title,
    firstControl = dialog.FirstControl,
    defaultControl = dialog.DefaultControl,
    cancelControl = dialog.CancelControl,
    bannerTitle = "FILL: name of the banner's title Text control",
    bannerText = "FILL: name of the banner's description Text control",
    bannerBitmap = frame.FirstOrDefault(c => c.Type == "Bitmap")?.Name,
    bodyTop = bannerLine.Y + bannerLine.Height + 8,
    bodyBottom = bottomLine.Y - 8,
    properties,
    textStyles = package.TextStyles.Values.Where(s => styles.Contains(s.Name)).OrderBy(s => s.Name).Select(s => new { s.Name, s.FaceName, s.Size, s.Color, StyleBits = (int)s.Bits }),
    controls = frame.Select(c => new { c.Name, c.Type, c.X, c.Y, c.Width, c.Height, c.Attributes, c.Property, c.Text, c.Next }),
};

File.WriteAllText(output, JsonSerializer.Serialize(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"Wrote {output}");
