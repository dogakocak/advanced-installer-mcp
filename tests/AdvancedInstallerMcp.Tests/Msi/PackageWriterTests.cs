using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Msi;

public sealed class PackageWriterTests
{
    [Fact]
    public void Writes_rows_that_read_back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");
        var rows = new PackageRows();
        rows.Properties.Add(("MODE", "a"));
        rows.Dialogs.Add(new DialogRow("Dlg", 370, 270, 3, "Title", "Box", null, null));
        rows.Controls.Add(new ControlRow("Dlg", "Box", "ComboBox", 10, 10, 100, 60, 3, "MODE", null, null));
        rows.ComboBoxItems.Add(new ListItemRow("MODE", 1, "a", "First"));
        rows.CustomActions.Add(new CustomActionRow("SetX", 51, "X", "[MODE]"));
        rows.ExecuteSequence.Add(new SequenceRow("SetX", null, 100));

        try
        {
            PackageWriter.Write(path, rows);

            using var database = MsiDatabase.OpenReadOnly(path);
            database.Query("SELECT `Text` FROM `ComboBox` WHERE `Property` = ?", r => r.GetString(1), "MODE")
                .ShouldBe(["First"]);
            database.Query("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = ?", r => r.GetInteger(1), "SetX")
                .ShouldBe([100]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Tables_without_rows_are_not_created()
    {
        var path = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");
        try
        {
            PackageWriter.Write(path, new PackageRows());

            using var database = MsiDatabase.OpenReadOnly(path);
            database.HasTable("ComboBox").ShouldBeFalse();
            database.HasTable("InstallExecuteSequence").ShouldBeFalse();
            database.HasTable("Dialog").ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
