namespace AdvancedInstallerMcp.Msi;

// Writes PackageRows into a new MSI with the standard table definitions (column types as in the Windows Installer
// docs). Used by mock_dialog (ADR 0008) and by the tests' generated packages.
public static class PackageWriter
{
    public static void Write(string path, PackageRows rows)
    {
        using var database = MsiDatabase.Create(path);

        database.Execute("CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL LOCALIZABLE PRIMARY KEY `Property`)");
        foreach (var (name, value) in rows.Properties)
        {
            database.Execute("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)", name, value);
        }

        if (rows.InstallPackage)
        {
            database.Execute("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)", "ProductCode", NewGuid());
            database.SetPackageSummary(NewGuid());
        }

        WriteSequence(database, "InstallUISequence", rows.UiSequence);
        WriteSequence(database, "InstallExecuteSequence", rows.ExecuteSequence);

        if (rows.LaunchConditions.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL " +
                "LOCALIZABLE PRIMARY KEY `Condition`)");
            foreach (var row in rows.LaunchConditions)
            {
                database.Execute("INSERT INTO `LaunchCondition` (`Condition`, `Description`) VALUES (?, ?)", row.Condition, row.Description);
            }
        }

        if (rows.CustomActions.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), " +
                "`Target` CHAR(255) PRIMARY KEY `Action`)");
            foreach (var row in rows.CustomActions)
            {
                database.Execute("INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) VALUES (?, ?, ?, ?)",
                    row.Action, row.Type, row.Source, row.Target);
            }
        }

        if (rows.WithUiTables)
        {
            WriteUiTables(database, rows);
        }

        if (rows.EmbeddedUi.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `MsiEmbeddedUI` (`MsiEmbeddedUI` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, " +
                "`Attributes` SHORT NOT NULL, `MessageFilter` LONG, `Data` OBJECT PRIMARY KEY `MsiEmbeddedUI`)");
            foreach (var row in rows.EmbeddedUi)
            {
                database.Execute("INSERT INTO `MsiEmbeddedUI` (`MsiEmbeddedUI`, `FileName`, `Attributes`) VALUES (?, ?, ?)",
                    row.Name, row.FileName, row.Attributes);
            }
        }

        if (rows.Binaries.Count > 0)
        {
            database.Execute("CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)");
            foreach (var (name, data) in rows.Binaries)
            {
                // msi.dll takes a stream only from a file.
                var file = Path.GetTempFileName();
                try
                {
                    File.WriteAllBytes(file, data);
                    database.Execute("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)", name, new FileInfo(file));
                }
                finally
                {
                    File.Delete(file);
                }
            }
        }

        database.Commit();
    }

    private static string NewGuid() => $"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}";

    private static void WriteSequence(MsiDatabase database, string table, List<SequenceRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        database.Execute($"CREATE TABLE `{table}` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)");
        foreach (var row in rows)
        {
            database.Execute($"INSERT INTO `{table}` (`Action`, `Condition`, `Sequence`) VALUES (?, ?, ?)", row.Action, row.Condition, row.Sequence);
        }
    }

    private static void WriteListItems(MsiDatabase database, string table, List<ListItemRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        database.Execute(
            $"CREATE TABLE `{table}` (`Property` CHAR(72) NOT NULL, `Order` SHORT NOT NULL, `Value` CHAR(64) NOT NULL, " +
            "`Text` CHAR(64) LOCALIZABLE PRIMARY KEY `Property`, `Order`)");
        foreach (var row in rows)
        {
            database.Execute($"INSERT INTO `{table}` (`Property`, `Order`, `Value`, `Text`) VALUES (?, ?, ?, ?)",
                row.Property, row.Order, row.Value, row.Text);
        }
    }

    private static void WriteUiTables(MsiDatabase database, PackageRows rows)
    {
        database.Execute(
            "CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, " +
            "`Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)");
        database.Execute(
            "CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, " +
            "`Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, " +
            "`Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)");
        database.Execute(
            "CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, " +
            "`X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, " +
            "`Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE " +
            "PRIMARY KEY `Dialog_`, `Control`)");

        foreach (var row in rows.TextStyles)
        {
            database.Execute(
                "INSERT INTO `TextStyle` (`TextStyle`, `FaceName`, `Size`, `Color`, `StyleBits`) VALUES (?, ?, ?, ?, ?)",
                row.Name, row.FaceName, row.Size, row.Color, row.StyleBits);
        }

        foreach (var row in rows.Dialogs)
        {
            database.Execute(
                "INSERT INTO `Dialog` (`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, " +
                "`Control_First`, `Control_Default`, `Control_Cancel`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                row.Name, 50, 50, row.Width, row.Height, row.Attributes, row.Title, row.FirstControl, row.DefaultControl,
                row.CancelControl);
        }

        foreach (var row in rows.Controls)
        {
            database.Execute(
                "INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, " +
                "`Text`, `Control_Next`, `Help`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                row.Dialog, row.Name, row.Type, row.X, row.Y, row.Width, row.Height, row.Attributes, row.Property, row.Text,
                row.Next, null);
        }

        if (rows.ControlConditions.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `ControlCondition` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, " +
                "`Action` CHAR(50) NOT NULL, `Condition` CHAR(255) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Action`, `Condition`)");
            foreach (var row in rows.ControlConditions)
            {
                database.Execute(
                    "INSERT INTO `ControlCondition` (`Dialog_`, `Control_`, `Action`, `Condition`) VALUES (?, ?, ?, ?)",
                    row.Dialog, row.Control, row.Action, row.Condition);
            }
        }

        if (rows.ControlEvents.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, " +
                "`Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT " +
                "PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)");
            foreach (var row in rows.ControlEvents)
            {
                database.Execute(
                    "INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) " +
                    "VALUES (?, ?, ?, ?, ?, ?)",
                    row.Dialog, row.Control, row.Event, row.Argument, row.Condition, row.Ordering);
            }
        }

        if (rows.RadioButtons.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `RadioButton` (`Property` CHAR(72) NOT NULL, `Order` SHORT NOT NULL, `Value` CHAR(64) NOT NULL, " +
                "`X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, " +
                "`Text` CHAR(0) LOCALIZABLE, `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Property`, `Order`)");
            foreach (var row in rows.RadioButtons)
            {
                database.Execute(
                    "INSERT INTO `RadioButton` (`Property`, `Order`, `Value`, `X`, `Y`, `Width`, `Height`, `Text`) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    row.Property, row.Order, row.Value, row.X, row.Y, row.Width, row.Height, row.Text);
            }
        }

        if (rows.EventMappings.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, " +
                "`Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)");
            foreach (var row in rows.EventMappings)
            {
                database.Execute(
                    "INSERT INTO `EventMapping` (`Dialog_`, `Control_`, `Event`, `Attribute`) VALUES (?, ?, ?, ?)",
                    row.Dialog, row.Control, row.Event, row.Attribute);
            }
        }

        WriteListItems(database, "ComboBox", rows.ComboBoxItems);
        WriteListItems(database, "ListBox", rows.ListBoxItems);
    }
}
