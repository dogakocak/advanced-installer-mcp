using System.Text.Json;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Mock;

public sealed record FrameControl(
    string Name, string Type, int X, int Y, int Width, int Height, int Attributes, string? Property, string? Text, string? Next);

// The rows every Advanced Installer wizard page shares (banner, lines, Back/Next/Cancel), its size, fonts and the
// properties its texts name, captured from a package Advanced Installer built (tools/frame/capture-wizard-frame.cs).
// mock_dialog puts the page body between BodyTop and BodyBottom. BannerTitle / BannerText / BannerBitmap name controls
// in Controls; BannerBinary is the Binary row the banner shows (not shipped: the mock writes a plain picture).
public sealed record WizardFrame(
    int Width, int Height, string Title, string FirstControl, string DefaultControl, string CancelControl,
    string BannerTitle, string BannerText, string BannerBitmap, string BannerBinary, int BodyTop, int BodyBottom,
    IReadOnlyDictionary<string, string> Properties, IReadOnlyList<TextStyleRow> TextStyles,
    IReadOnlyList<FrameControl> Controls)
{
    private static readonly Lazy<WizardFrame> Instance = new(() =>
    {
        using var stream = typeof(WizardFrame).Assembly.GetManifestResourceStream("WizardFrame.json")
            ?? throw new InvalidOperationException("WizardFrame.json is not embedded.");
        return JsonSerializer.Deserialize<WizardFrame>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("WizardFrame.json is empty.");
    });

    public static WizardFrame Load() => Instance.Value;
}
