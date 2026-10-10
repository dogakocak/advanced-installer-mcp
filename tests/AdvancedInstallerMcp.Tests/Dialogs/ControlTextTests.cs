using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Tests.Dialogs;

public sealed class ControlTextTests
{
    private static readonly Dictionary<string, string> Properties = new()
    {
        ["DlgTitleFont"] = "{&DlgFontBold8}",
        ["ProductName"] = "Sample",
    };

    [Theory]
    [InlineData("Plain text", null, "Plain text", false)]
    [InlineData("{\\DlgFont8}Hello", "DlgFont8", "Hello", false)]
    [InlineData("{&DlgFontBold8}Hello", "DlgFontBold8", "Hello", false)]
    [InlineData("{\\DlgFont8}{&DlgFontBold8}Hello", "DlgFontBold8", "Hello", false)]
    [InlineData("[DlgTitleFont]Welcome", "DlgFontBold8", "Welcome", false)]
    [InlineData("[ProductName] Setup", null, "[ProductName] Setup", true)]
    [InlineData("{\\DlgFont8}Welcome to [ProductName]", "DlgFont8", "Welcome to [ProductName]", true)]
    [InlineData("[UnknownProperty]Text", null, "[UnknownProperty]Text", true)]
    [InlineData("{\\Unclosed", null, "{\\Unclosed", false)]
    [InlineData("", null, "", false)]
    [InlineData("{\\rtf1\\ansi {\\fonttbl}License}", null, "{\\rtf1\\ansi {\\fonttbl}License}", false)]
    public void Splits_style_prefix_from_display_text(string text, string? style, string display, bool runtime)
    {
        ControlText.Parse(text, Properties).ShouldBe(new ControlText(style, display, runtime));
    }
}
