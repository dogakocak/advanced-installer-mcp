using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Msi;

public sealed class MsiDatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");

    public MsiDatabaseTests()
    {
        // A cut-down Control table: enough columns to cover strings, integers and nulls.
        using var database = MsiDatabase.Create(_path);
        database.Execute(
            "CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, " +
            "`Type` CHAR(20) NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT, `Text` LONGCHAR " +
            "PRIMARY KEY `Dialog_`, `Control`)");

        const string insert = "INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `Width`, `Height`, `Text`) VALUES (?, ?, ?, ?, ?, ?)";
        database.Execute(insert, "ConnectionDlg", "HintText", "Text", 230, 18, "Enter the server name and port.");
        database.Execute(insert, "ConnectionDlg", "TrustCheck", "CheckBox", 125, 12, "Trust server certificate");
        database.Execute(insert, "ConnectionDlg", "Line", "Line", 370, null, null);
        database.Execute(insert, "WelcomeDlg", "LongText", "Text", 300, 40, new string('x', 600));
        database.Commit();
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Query_reads_string_and_integer_columns_filtered_by_parameter()
    {
        using var database = MsiDatabase.OpenReadOnly(_path);

        // MSI SQL ORDER BY does not sort strings alphabetically (it kept insertion order here), so order in C#.
        var controls = database.Query(
            "SELECT `Control`, `Type`, `Width`, `Height` FROM `Control` WHERE `Dialog_` = ?",
            r => (Name: r.GetString(1), Type: r.GetString(2), Width: r.GetInteger(3), Height: r.GetInteger(4)),
            "ConnectionDlg");

        controls.OrderBy(c => c.Name, StringComparer.Ordinal).ShouldBe([
            ("HintText", "Text", 230, 18),
            ("Line", "Line", 370, null),
            ("TrustCheck", "CheckBox", 125, 12),
        ]);
    }

    [Fact]
    public void Null_fields_read_as_null_integer_and_empty_string()
    {
        using var database = MsiDatabase.OpenReadOnly(_path);

        var line = database.Query(
            "SELECT `Height`, `Text` FROM `Control` WHERE `Control` = ?",
            r => (Height: r.GetInteger(1), Text: r.GetString(2), TextIsNull: r.IsNull(2)),
            "Line").ShouldHaveSingleItem();

        line.ShouldBe((null, "", true));
    }

    [Fact]
    public void Strings_longer_than_the_first_buffer_are_read_in_full()
    {
        using var database = MsiDatabase.OpenReadOnly(_path);

        var text = database.Query("SELECT `Text` FROM `Control` WHERE `Control` = 'LongText'", r => r.GetString(1))
            .ShouldHaveSingleItem();

        text.ShouldBe(new string('x', 600));
    }

    [Fact]
    public void Invalid_query_throws_with_the_msi_error_code()
    {
        using var database = MsiDatabase.OpenReadOnly(_path);

        var error = Should.Throw<MsiException>(() => database.Query("SELECT FROM WHERE", r => r.GetString(1)));

        error.ErrorCode.ShouldBe(1615u); // ERROR_BAD_QUERY_SYNTAX
    }

    [Fact]
    public void Opening_a_missing_package_throws_file_not_found()
    {
        Should.Throw<FileNotFoundException>(() => MsiDatabase.OpenReadOnly(_path + ".missing"));
    }
}
