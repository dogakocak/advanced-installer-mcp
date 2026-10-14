using AdvancedInstallerMcp.Render;

namespace AdvancedInstallerMcp.Drive;

// What drive_dialog plays (ADR 0007): property values the user enters, and clicks.
public abstract record DriveStep;

// The user typed into an edit, picked a radio button or (un)checked a box. An unchecked box is "" (or null): Windows
// Installer leaves its property empty.
public sealed record SetStep(IReadOnlyDictionary<string, string?> Values) : DriveStep
{
    public override string ToString() => $"set {string.Join(", ", Values.Keys)}";
}

public sealed record ClickStep(string Control) : DriveStep
{
    public override string ToString() => $"click {Control}";
}

// Dialog: the page the steps start on; null = the first page the wizard opens with. Maintenance: the package is
// installed (Change, Repair, Remove). Properties: as on the msiexec command line. ActionResults: for a custom action
// the restricted engine refuses, the property values it would set.
public sealed record DriveRequest(
    IReadOnlyList<DriveStep> Steps,
    string? Dialog = null,
    bool Maintenance = false,
    IReadOnlyDictionary<string, string?>? Properties = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? ActionResults = null);

public sealed record PropertyChange(string Name, string Old, string New);

public sealed record ActionReport(string Action, string Outcome);

public sealed record EventReport(string Event, string Argument, string Condition, string Outcome);

public sealed record ControlChange(string Control, bool Visible, bool Enabled);

// FirstPage: the first modal dialog of InstallUISequence whose condition holds; null when the sequence shows none.
public sealed record OpeningReport(
    string? FirstPage,
    IReadOnlyList<ActionReport> Actions,
    IReadOnlyList<PropertyChange> Properties);

// Blocked: the click did nothing because the control is disabled or hidden.
public sealed record StepReport(
    string Step,
    string Dialog,
    string? Blocked,
    IReadOnlyList<EventReport> Events,
    IReadOnlyList<PropertyChange> Properties,
    IReadOnlyList<string> DialogsOpened,
    IReadOnlyList<ControlChange> Controls);

// Dialog: the page after the last step. Ended: the EndDialog argument when the wizard ended. State: the page's
// controls as the engine evaluated them (drawn by the renderer).
public sealed record DriveResult(
    OpeningReport Opening,
    IReadOnlyList<StepReport> Steps,
    string Dialog,
    string? Ended,
    DialogState State);

public sealed class DriveException(string message) : Exception(message);

public static class ActionReports
{
    public const string Ran = "ran";
    public const string Refused =
        "not run: it runs package code, which the restricted engine refuses; outcome unknown (pass actionResults)";
    public const string ResultsApplied = "not run: it runs package code; actionResults applied";
    public const string Unknown = "not run: no such action";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class EventReports
{
    public const string PropertySet = "set";
    public const string ActionRan = ActionReports.Ran;
    public const string ActionRefused = ActionReports.Refused;
    public const string ActionResultsApplied = ActionReports.ResultsApplied;
    public const string ActionUnknown = ActionReports.Unknown;
    public const string ActionFailed = ActionReports.Failed;
    public const string ConditionFalse = "skipped: condition false";
    public const string ConditionError = "skipped: condition does not parse";
    public const string PageReplaced = "page replaced";
    public const string DialogOpened = "dialog opened";
    public const string NoSuchDialog = "skipped: no such dialog";
    public const string WizardEnded = "wizard ended";
    public const string NotPlayed = "not played in this version";
}
