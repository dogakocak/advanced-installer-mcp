namespace AdvancedInstallerMcp.Tests.Fixtures;

// A small wizard for drive_dialog: Welcome (or Maint when installed) -> SqlDlg -> DoneDlg.
public static class ConnectionWizard
{
    public const string Welcome = "WelcomeDlg";
    public const string Maint = "MaintDlg";
    public const string Sql = "SqlDlg";
    public const string Fail = "SqlFailDlg";
    public const string Done = "DoneDlg";

    // A connection page like the measured setup UI task: a trust check box set by a property-setting action when
    // the wizard opens, a Test button whose real check is a script (refused by the restricted engine), and Next
    // enabled only after a successful test.
    public static TestPackage Create()
    {
        var package = new TestPackage()
            .AsInstallPackage()
            .WithProperty("DefaultUIFont", "DlgFont8")
            .WithProperty("MsiHiddenProperties", "DB_PASSWORD")
            .WithTextStyle("DlgFont8", "Tahoma", 8)
            .WithCustomAction("InitTrust", 51, "UI_TRUST", "1")
            .WithCustomAction("TestSql", 38, null, "Session.Property(\"SQL_OK\") = \"1\"")
            .WithUiSequence("InitTrust", 10)
            .WithUiSequence("PrepareDlg", 20)
            .WithDialog("PrepareDlg", 370, 270, "Next", attributes: 1) // modeless: msiexec shows it and goes on
            .WithControl("PrepareDlg", "Next", "PushButton", 236, 243, 56, 17, "Next")
            .WithUiSequence(Welcome, 100, "NOT Installed")
            .WithUiSequence(Maint, 101, "Installed");

        foreach (var name in new[] { Welcome, Maint, Done })
        {
            package
                .WithDialog(name, 370, 270, "Next", defaultControl: "Next")
                .WithControl(name, "Next", "PushButton", 236, 243, 56, 17, "Next");
        }

        return package
            .WithControlEvent(Welcome, "Next", "NewDialog", Sql)
            .WithControlEvent(Maint, "Next", "NewDialog", Sql)
            .WithControlEvent(Done, "Next", "EndDialog", "Return")
            .WithDialog(Sql, 370, 270, "ServerEdit", defaultControl: "Next")
            .WithControl(Sql, "ServerEdit", "Edit", 20, 60, 200, 18, property: "DB_SERVER", next: "PasswordEdit")
            .WithControl(Sql, "PasswordEdit", "Edit", 20, 84, 200, 18, attributes: 0x200003, property: "DB_PASSWORD",
                next: "TrustCheck")
            .WithControl(Sql, "TrustCheck", "CheckBox", 20, 110, 200, 12, "Trust server certificate",
                property: "UI_TRUST", next: "TestButton")
            .WithControl(Sql, "TestButton", "PushButton", 230, 108, 80, 17, "Test Connection", next: "Next")
            .WithControl(Sql, "Next", "PushButton", 236, 243, 56, 17, "Next", next: "ServerEdit")
            .WithControlEvent(Sql, "TestButton", "[DB_TRUST]", "True", "UI_TRUST", 1)
            .WithControlEvent(Sql, "TestButton", "[DB_TRUST]", "False", "NOT UI_TRUST", 2)
            .WithControlEvent(Sql, "TestButton", "[SQL_OK]", "0", null, 3)
            .WithControlEvent(Sql, "TestButton", "DoAction", "TestSql", null, 4)
            .WithControlEvent(Sql, "TestButton", "[SQL_TESTED]", "1", "SQL_OK = \"1\"", 5)
            .WithControlEvent(Sql, "TestButton", "SpawnDialog", Fail, "SQL_OK <> \"1\"", 6)
            .WithControlEvent(Sql, "Next", "NewDialog", Done, "SQL_TESTED = \"1\"")
            .WithControlCondition(Sql, "Next", "Disable", "SQL_TESTED <> \"1\"")
            .WithControlCondition(Sql, "Next", "Enable", "SQL_TESTED = \"1\"")
            .WithDialog(Fail, 200, 80, "OK")
            .WithControl(Fail, "OK", "PushButton", 70, 50, 56, 17, "OK");
    }
}
