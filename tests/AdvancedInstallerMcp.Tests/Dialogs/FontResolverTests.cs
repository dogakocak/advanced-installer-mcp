using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Tests.Dialogs;

public sealed class FontResolverTests
{
    private static readonly Dictionary<string, TextStyle> Styles = new()
    {
        ["DlgFont8"] = new("DlgFont8", "Tahoma", 8, null, TextStyleBits.None),
        ["DlgFontBold8"] = new("DlgFontBold8", "Tahoma", 8, null, TextStyleBits.Bold),
    };

    [Fact]
    public void Own_style_wins()
    {
        FontResolver.Resolve("DlgFontBold8", Styles, "DlgFont8")
            .ShouldBe(new ResolvedFont("Tahoma", 8, TextStyleBits.Bold, FontSource.TextStyle, FontIssue.None));
    }

    [Fact]
    public void Without_own_style_uses_DefaultUIFont()
    {
        FontResolver.Resolve(null, Styles, "DlgFont8")
            .ShouldBe(new ResolvedFont("Tahoma", 8, TextStyleBits.None, FontSource.DefaultUIFont, FontIssue.None));
    }

    [Fact]
    public void Unknown_own_style_falls_back_to_DefaultUIFont_and_says_so()
    {
        FontResolver.Resolve("Missing", Styles, "DlgFont8")
            .ShouldBe(new ResolvedFont("Tahoma", 8, TextStyleBits.None, FontSource.DefaultUIFont, FontIssue.TextStyleNotFound));
    }

    [Fact]
    public void Missing_DefaultUIFont_uses_the_fallback_and_says_so()
    {
        FontResolver.Resolve(null, Styles, null)
            .ShouldBe(new ResolvedFont("Segoe UI", 9, TextStyleBits.None, FontSource.Fallback, FontIssue.DefaultUIFontMissing));
    }

    [Fact]
    public void DefaultUIFont_naming_an_unknown_style_uses_the_fallback_and_says_so()
    {
        FontResolver.Resolve(null, Styles, "Missing")
            .ShouldBe(new ResolvedFont("Segoe UI", 9, TextStyleBits.None, FontSource.Fallback, FontIssue.DefaultUIFontStyleNotFound));
    }
}
