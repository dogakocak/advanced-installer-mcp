using System.Text.RegularExpressions;

namespace AdvancedInstallerMcp.Dialogs;

// The text a user sees for a Formatted string, as far as the package alone can tell.
//   "[ButtonText_Next]"   -> the value in the Property table, e.g. "&Next >"
//   "[SOME_RUNTIME_PROP]" -> "" (not in the table; set at run time, so the real text is at least the rest)
//   "[\[]"                -> "["
//   "[#file]", "[%PATH]"  -> "" (file paths, environment variables: only known at run time)
// Unresolved counts the references that became "", so the result is a lower bound only when it is above zero.
public static partial class FormattedText
{
    public static (string Text, int Resolved, int Unresolved) Resolve(string text, IReadOnlyDictionary<string, string> properties)
    {
        var resolved = 0;
        var unresolved = 0;

        var result = Reference().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (name.Length == 2 && name[0] == '\\')
            {
                return name[1..];
            }

            // One level only: a value that is itself formatted would need run-time context to expand.
            if (properties.TryGetValue(name, out var value) && !Reference().IsMatch(value))
            {
                resolved++;
                return value;
            }

            unresolved++;
            return "";
        });

        return (result, resolved, unresolved);
    }

    // The escape first: in "[\]]" the other branch would stop at the escaped bracket.
    [GeneratedRegex(@"\[(\\.|[^\[\]]+)\]")]
    private static partial Regex Reference();
}
