using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Tests.Dialogs;

public sealed class FormattedTextTests
{
    private static readonly Dictionary<string, string> Properties = new()
    {
        ["ProductName"] = "Sample App",
        ["ButtonText_Next"] = "&Next >",
        ["Nested"] = "[ProductName] Setup",
    };

    [Theory]
    [InlineData("Welcome to [ProductName]", "Welcome to Sample App", 1, 0)]
    [InlineData("[ButtonText_Next]", "&Next >", 1, 0)]
    [InlineData("Path: [INSTALLDIR]", "Path: ", 0, 1)]
    [InlineData("[#file.exe] and [%PATH]", " and ", 0, 2)]
    [InlineData("[Nested]", "", 0, 1)]
    [InlineData("A [\\[]literal[\\]] bracket", "A [literal] bracket", 0, 0)]
    [InlineData("No references", "No references", 0, 0)]
    public void Resolves_what_the_package_knows(string text, string expected, int resolved, int unresolved)
    {
        FormattedText.Resolve(text, Properties).ShouldBe((expected, resolved, unresolved));
    }

    // For a picture: what the package does not know stays visible as written.
    [Theory]
    [InlineData("Welcome to [ProductName]", "Welcome to Sample App", new string[0])]
    [InlineData("Path: [INSTALLDIR]", "Path: [INSTALLDIR]", new[] { "INSTALLDIR" })]
    [InlineData("[Nested]", "[Nested]", new[] { "Nested" })]
    [InlineData("A [\\[]b", "A [b", new string[0])]
    public void Display_keeps_unresolved_references(string text, string expected, string[] unresolved)
    {
        var (display, names) = FormattedText.ResolveForDisplay(text, Properties);

        display.ShouldBe(expected);
        names.ShouldBe(unresolved);
    }
}
