using System.Text.RegularExpressions;

namespace AdvancedInstallerMcp.Dialogs;

// A control's Text split into its font style and the text the user sees.
//   "{\DlgFont8}Hello"                       -> style DlgFont8
//   "[DlgTitleFont]Welcome" with DlgTitleFont = "{&DlgFontBold8}"  -> style DlgFontBold8
// HasRuntimeText: the visible text still contains [Property] references, so its length is only a lower bound.
public sealed partial record ControlText(string? StyleName, string DisplayText, bool HasRuntimeText)
{
    public static ControlText Parse(string text, IReadOnlyDictionary<string, string> properties)
    {
        string? style = null;
        var position = 0;

        while (position < text.Length)
        {
            if (TryReadStyleToken(text, position, out var name, out var next))
            {
                style = name;
                position = next;
            }
            else if (TryReadStyleProperty(text, position, properties, out name, out next))
            {
                style = name ?? style;
                position = next;
            }
            else
            {
                break;
            }
        }

        var display = text[position..];
        return new ControlText(style, display, PropertyReference().IsMatch(display));
    }

    // {\Style} or {&Style}
    private static bool TryReadStyleToken(string text, int position, out string name, out int next)
    {
        name = "";
        next = position;

        if (position + 2 >= text.Length || text[position] != '{' || (text[position + 1] != '\\' && text[position + 1] != '&'))
        {
            return false;
        }

        var close = text.IndexOf('}', position + 2);
        if (close < 0)
        {
            return false;
        }

        // Must be an Identifier: rules out RTF such as "{\rtf1\ansi ...}" in ScrollableText controls.
        name = text[(position + 2)..close];
        next = close + 1;
        return Identifier().IsMatch(name);
    }

    // [Property] whose value consists only of style tokens.
    private static bool TryReadStyleProperty(
        string text, int position, IReadOnlyDictionary<string, string> properties, out string? name, out int next)
    {
        name = null;
        next = position;

        if (text[position] != '[')
        {
            return false;
        }

        var close = text.IndexOf(']', position + 1);
        if (close < 0 || !properties.TryGetValue(text[(position + 1)..close], out var value))
        {
            return false;
        }

        var parsed = Parse(value, EmptyProperties);
        if (parsed.DisplayText.Length > 0 || parsed.StyleName is null)
        {
            return false;
        }

        name = parsed.StyleName;
        next = close + 1;
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties = new Dictionary<string, string>();

    [GeneratedRegex(@"\[[^\[\]]+\]")]
    private static partial Regex PropertyReference();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex Identifier();
}
