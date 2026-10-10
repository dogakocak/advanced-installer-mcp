namespace AdvancedInstallerMcp.Dialogs;

public enum FontSource
{
    TextStyle,
    DefaultUIFont,
    Fallback,
}

public enum FontIssue
{
    None,
    DefaultUIFontMissing,
    DefaultUIFontStyleNotFound,
    TextStyleNotFound,
}

public sealed record ResolvedFont(string FaceName, int Size, TextStyleBits Bits, FontSource Source, FontIssue Issue);

// ADR 0003: the control's own style, otherwise DefaultUIFont, otherwise our fallback (reported, never silent).
public static class FontResolver
{
    public static readonly TextStyle Fallback = new("(fallback)", "Segoe UI", 9, null, TextStyleBits.None);

    public static ResolvedFont Resolve(string? styleName, IReadOnlyDictionary<string, TextStyle> styles, string? defaultUIFont)
    {
        var issue = FontIssue.None;

        if (styleName is not null)
        {
            if (styles.TryGetValue(styleName, out var own))
            {
                return From(own, FontSource.TextStyle, issue);
            }

            issue = FontIssue.TextStyleNotFound;
        }

        if (string.IsNullOrEmpty(defaultUIFont))
        {
            return From(Fallback, FontSource.Fallback, Worst(issue, FontIssue.DefaultUIFontMissing));
        }

        return styles.TryGetValue(defaultUIFont, out var fallbackStyle)
            ? From(fallbackStyle, FontSource.DefaultUIFont, issue)
            : From(Fallback, FontSource.Fallback, Worst(issue, FontIssue.DefaultUIFontStyleNotFound));
    }

    // The control's own missing style is the more specific problem; report that one.
    private static FontIssue Worst(FontIssue first, FontIssue second) => first != FontIssue.None ? first : second;

    private static ResolvedFont From(TextStyle style, FontSource source, FontIssue issue) =>
        new(style.FaceName, style.Size, style.Bits, source, issue);
}
