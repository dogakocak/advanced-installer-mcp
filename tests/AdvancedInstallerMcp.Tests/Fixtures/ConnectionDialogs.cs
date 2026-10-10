namespace AdvancedInstallerMcp.Tests.Fixtures;

// The dialog pair from ADR 0003: a database connection page with known layout problems and its fixed twin.
// Sizes are installer units; the hint needs two lines in Tahoma 8 pt (13 px each, 26 px in total).
public static class ConnectionDialogs
{
    public const string Broken = "ConnectionDlg";
    public const string Fixed = "ConnectionFixedDlg";

    public const string Hint = "Enter the server name and port of the database. The connection is tested first.";
    public const string TrustLabel = "Trust server certificate";

    public static TestPackage Create() =>
        new TestPackage()
            .WithProperty("DefaultUIFont", "DlgFont8")
            .WithProperty("DlgTitleFont", "{&DlgFontBold8}")
            .WithTextStyle("DlgFont8", "Tahoma", 8)
            .WithTextStyle("DlgFontBold8", "Tahoma", 8, styleBits: 1)
            .WithDialog(Broken, 370, 270, "ServerEdit", "[ProductName] Setup", "Next", "Cancel")
            // 18 units = 24 px at 96 DPI: the second line of the hint is cut off.
            .WithControl(Broken, "Title", "Text", 15, 6, 200, 15, "[DlgTitleFont]Database connection")
            .WithControl(Broken, "Hint", "Text", 20, 60, 230, 18, Hint)
            .WithControl(Broken, "ServerEdit", "Edit", 20, 80, 200, 18, property: "DB_SERVER", next: "TrustCheck")
            // Overlaps ServerEdit: starts at y=90, inside 80..98.
            .WithControl(Broken, "TrustCheck", "CheckBox", 20, 90, 125, 12, TrustLabel, property: "DB_TRUST", next: "Next")
            .WithControl(Broken, "Welcome", "Text", 20, 120, 300, 12, "{\\DlgFont8}Welcome to [ProductName]")
            .WithControl(Broken, "Next", "PushButton", 236, 243, 56, 17, "&Next >", next: "Cancel")
            .WithControl(Broken, "Cancel", "PushButton", 304, 243, 56, 17, "Cancel", next: "ServerEdit")
            .WithDialog(Fixed, 370, 270, "ServerEdit", "[ProductName] Setup", "Next", "Cancel")
            .WithControl(Fixed, "Title", "Text", 15, 6, 200, 15, "[DlgTitleFont]Database connection")
            .WithControl(Fixed, "Hint", "Text", 20, 60, 230, 20, Hint)
            .WithControl(Fixed, "ServerEdit", "Edit", 20, 82, 200, 18, property: "DB_SERVER", next: "TrustCheck")
            .WithControl(Fixed, "TrustCheck", "CheckBox", 20, 104, 125, 12, TrustLabel, property: "DB_TRUST", next: "Next")
            .WithControl(Fixed, "Next", "PushButton", 236, 243, 56, 17, "&Next >", next: "Cancel")
            .WithControl(Fixed, "Cancel", "PushButton", 304, 243, 56, 17, "Cancel", next: "ServerEdit");
}
