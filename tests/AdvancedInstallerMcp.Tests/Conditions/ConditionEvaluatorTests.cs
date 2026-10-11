using AdvancedInstallerMcp.Conditions;

namespace AdvancedInstallerMcp.Tests.Conditions;

// Rules from Microsoft Learn, "Conditional Statement Syntax".
public sealed class ConditionEvaluatorTests
{
    private static readonly Dictionary<string, string> Properties = new()
    {
        ["SET"] = "yes",
        ["ZERO"] = "0",
        ["VersionNT"] = "603",
        ["NAME"] = "Contoso Setup",
        ["EMPTY"] = "",
    };

    private static bool? Evaluate(string condition) => ConditionEvaluator.Evaluate(condition, Properties);

    [Theory]
    [InlineData("SET", true)]
    [InlineData("ZERO", true)] // a property is true when it has any value, "0" included
    [InlineData("EMPTY", false)]
    [InlineData("MISSING", false)] // nonexistent properties are empty strings
    [InlineData("NOT SET", false)]
    [InlineData("not MISSING", true)] // operators are not case sensitive
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("\"text\"", true)]
    [InlineData("\"\"", false)]
    public void Single_values(string condition, bool expected) => Evaluate(condition).ShouldBe(expected);

    [Theory]
    [InlineData("SET = \"yes\"", true)]
    [InlineData("SET = \"YES\"", false)] // values are case sensitive
    [InlineData("SET ~= \"YES\"", true)]
    [InlineData("SET <> \"no\"", true)]
    [InlineData("VersionNT >= 600", true)]
    [InlineData("VersionNT < 600", false)]
    [InlineData("VersionNT = 603", true)]
    [InlineData("NAME = 1", false)] // integer against a value that is not one: false ...
    [InlineData("NAME <> 1", true)] // ... except for <>
    [InlineData("MISSING = \"\"", true)]
    [InlineData("\"b\" > \"a\"", true)]
    public void Comparisons(string condition, bool expected) => Evaluate(condition).ShouldBe(expected);

    [Theory]
    [InlineData("NAME >< \"Setup\"", true)]
    [InlineData("NAME << \"Contoso\"", true)]
    [InlineData("NAME >> \"Setup\"", true)]
    [InlineData("NAME >> \"setup\"", false)]
    [InlineData("NAME ~>> \"setup\"", true)]
    [InlineData("6 >< 2", true)] // bitwise AND on integers
    [InlineData("4 >< 2", false)]
    [InlineData("131075 << 2", true)] // high 16 bits
    [InlineData("131075 >> 3", true)] // low 16 bits
    public void Substring_and_bitwise_operators(string condition, bool expected) => Evaluate(condition).ShouldBe(expected);

    [Theory]
    [InlineData("SET AND MISSING", false)]
    [InlineData("SET OR MISSING", true)]
    [InlineData("NOT SET OR SET", true)] // NOT binds tighter than OR
    [InlineData("NOT (SET OR SET)", false)]
    [InlineData("MISSING AND SET OR SET", true)] // AND binds tighter than OR
    [InlineData("SET XOR SET", false)]
    [InlineData("SET XOR MISSING", true)]
    [InlineData("MISSING EQV MISSING", true)]
    [InlineData("SET IMP MISSING", false)]
    [InlineData("MISSING IMP SET", true)]
    [InlineData("SET OR MISSING XOR SET", false)] // OR binds tighter than XOR
    public void Logical_operators_and_precedence(string condition, bool expected) => Evaluate(condition).ShouldBe(expected);

    // Install-time state the package rows cannot tell: the result is unknown, not false.
    [Theory]
    [InlineData("%PATH")]
    [InlineData("&MainFeature = 3")]
    [InlineData("!MainFeature = 3")]
    [InlineData("$Component = 3")]
    [InlineData("?Component = 3")]
    [InlineData("SET AND %TEMP")]
    public void Install_time_state_is_unknown(string condition) => Evaluate(condition).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("SET AND")]
    [InlineData("(SET")]
    [InlineData("\"unterminated")]
    [InlineData("SET = = 1")]
    [InlineData("1.5 = 1")]
    public void Malformed_conditions_are_unknown(string condition) => Evaluate(condition).ShouldBeNull();
}
