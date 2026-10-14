using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Msi;
using AdvancedInstallerMcp.Render;

namespace AdvancedInstallerMcp.Drive;

// Plays a package's dialogs in a restricted Windows Installer session (ADR 0007): the UI sequence up to the first
// page, then the caller's steps, each click as the control's ControlEvent rows. The engine evaluates every condition
// and runs built-in and property-setting actions; custom actions that run package code are refused by the engine and
// stand in with the caller's actionResults.
public static class DialogDriver
{
    private const string Masked = "***";

    // Longer values are cut in the report: Advanced Installer passes whole scripts through CustomActionData.
    private const int ShownCharacters = 120;

    public static DriveResult Drive(string msiPath, DriveRequest request)
    {
        using var database = MsiDatabase.OpenReadOnly(msiPath);
        var package = DialogPackage.Load(database);
        var tables = DriveTables.Load(database);
        using var session = MsiSession.Open(msiPath);

        var run = new Run(package, tables, session, request);
        return run.Play();
    }

    private sealed class Run(DialogPackage package, DriveTables tables, MsiSession session, DriveRequest request)
    {
        private readonly HashSet<string> _watched = Watched(tables, request);
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> _actionResults =
            request.ActionResults ?? new Dictionary<string, IReadOnlyDictionary<string, string?>>();

        private HashSet<string> _hidden = [];

        public DriveResult Play()
        {
            if (request.Maintenance)
            {
                session.SetProperty("Installed", "1");
            }

            foreach (var (name, value) in request.Properties ?? new Dictionary<string, string?>())
            {
                session.SetProperty(name, value ?? "");
            }

            var opening = Open();
            var dialog = request.Dialog ?? opening.FirstPage ?? throw new DriveException(
                "The UI sequence shows no page. Name the dialog to start on.");
            var current = FindDialog(dialog);

            string? ended = null;
            var steps = new List<StepReport>();
            foreach (var step in request.Steps)
            {
                if (ended is not null)
                {
                    throw new DriveException($"The wizard ended ({ended}) before step {steps.Count + 1} ({step}).");
                }

                var (report, next, end) = PlayStep(step, current);
                steps.Add(report);
                current = next;
                ended = end;
            }

            return new DriveResult(opening, steps, current.Name, ended, State(current));
        }

        // InstallUISequence from the start, as msiexec runs it before the first page: actions whose condition holds,
        // modeless dialogs skipped, up to the first modal dialog whose condition holds.
        private OpeningReport Open()
        {
            var before = Snapshot();
            var actions = new List<ActionReport>();
            string? firstPage = null;

            foreach (var row in tables.UiSequence)
            {
                if (row.Action == "ExecuteAction")
                {
                    break;
                }

                if (session.EvaluateCondition(row.Condition) != true)
                {
                    continue;
                }

                if (tables.IsDialog(row.Action))
                {
                    if (tables.IsModalDialog(row.Action))
                    {
                        firstPage = row.Action;
                        break;
                    }

                    continue;
                }

                actions.Add(new ActionReport(row.Action, DoAction(row.Action)));
            }

            _hidden = HiddenProperties();
            return new OpeningReport(firstPage, actions, Changes(before, Snapshot()));
        }

        private (StepReport Report, Dialog Next, string? Ended) PlayStep(DriveStep step, Dialog dialog)
        {
            var before = Snapshot();
            var controlsBefore = State(dialog);
            var events = new List<EventReport>();
            var opened = new List<string>();
            string? blocked = null;
            var next = dialog;
            string? ended = null;

            switch (step)
            {
                case SetStep set:
                    foreach (var (name, value) in set.Values)
                    {
                        session.SetProperty(name, value ?? "");
                    }

                    break;

                case ClickStep click:
                    var control = controlsBefore.Controls.SingleOrDefault(c => c.Control.Name == click.Control) ?? throw new DriveException(
                        $"{dialog.Name} has no control '{click.Control}'. Controls: " +
                        string.Join(", ", dialog.Controls.Select(c => c.Name)) + ".");
                    blocked = !control.Visible ? "hidden" : !control.Enabled ? "disabled" : null;
                    if (blocked is null)
                    {
                        (next, ended) = Click(dialog, click.Control, events, opened);
                    }

                    break;
            }

            var after = State(dialog);
            var controls = after.Controls
                .Zip(controlsBefore.Controls)
                .Where(pair => pair.First.Visible != pair.Second.Visible || pair.First.Enabled != pair.Second.Enabled)
                .Select(pair => new ControlChange(pair.First.Control.Name, pair.First.Visible, pair.First.Enabled))
                .ToList();

            var report = new StepReport(step.ToString()!, dialog.Name, blocked, events, Changes(before, Snapshot()), opened, controls);
            return (report, next, ended);
        }

        // Every row of the control in Ordering order, each only if its condition holds at that moment. A NewDialog
        // takes effect after the rest of the rows, as the page is replaced once the control's events are done.
        private (Dialog Next, string? Ended) Click(Dialog dialog, string control, List<EventReport> events, List<string> opened)
        {
            var next = dialog;
            string? ended = null;

            foreach (var row in tables.Events(dialog.Name, control))
            {
                var condition = session.EvaluateCondition(row.Condition);
                if (condition != true)
                {
                    events.Add(Report(row, condition is null ? EventReports.ConditionError : EventReports.ConditionFalse));
                    continue;
                }

                if (DriveTables.PropertyEvent(row.Event) is { } property)
                {
                    // The engine formats "{}" (a ControlEvent's way to set a property to nothing) as "".
                    session.SetProperty(property, session.Format(row.Argument));
                    events.Add(Report(row, EventReports.PropertySet));
                    continue;
                }

                switch (row.Event)
                {
                    case "DoAction":
                        events.Add(Report(row, DoAction(row.Argument)));
                        break;

                    case "NewDialog" when tables.IsDialog(row.Argument):
                        next = FindDialog(row.Argument);
                        events.Add(Report(row, EventReports.PageReplaced));
                        break;

                    case "SpawnDialog" or "SpawnWaitDialog" when tables.IsDialog(row.Argument):
                        opened.Add(row.Argument);
                        events.Add(Report(row, EventReports.DialogOpened));
                        break;

                    case "NewDialog" or "SpawnDialog" or "SpawnWaitDialog":
                        events.Add(Report(row, EventReports.NoSuchDialog));
                        break;

                    case "EndDialog":
                        ended = row.Argument;
                        events.Add(Report(row, EventReports.WizardEnded));
                        break;

                    default:
                        events.Add(Report(row, EventReports.NotPlayed));
                        break;
                }
            }

            return (next, ended);
        }

        private string DoAction(string action)
        {
            switch (session.DoAction(action))
            {
                case ActionOutcome.Ran:
                    return ActionReports.Ran;
                case ActionOutcome.Failed:
                    return ActionReports.Failed;
                case ActionOutcome.Cancelled:
                    return ActionReports.Cancelled;
            }

            if (!tables.RunsPackageCode(action))
            {
                return ActionReports.Unknown;
            }

            if (!_actionResults.TryGetValue(action, out var results))
            {
                return ActionReports.Refused;
            }

            foreach (var (name, value) in results)
            {
                session.SetProperty(name, value ?? "");
            }

            return ActionReports.ResultsApplied;
        }

        private Dialog FindDialog(string name) =>
            package.FindDialog(name) ?? throw new DriveException(
                $"The package has no dialog '{name}'. Dialogs: {string.Join(", ", package.DialogNames)}.");

        private DialogState State(Dialog dialog)
        {
            var properties = _watched
                .Select(name => (Name: name, Value: session.GetProperty(name)))
                .Where(p => p.Value.Length > 0)
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            return DialogState.Evaluate(package, dialog, properties, session.EvaluateCondition);
        }

        private Dictionary<string, string> Snapshot() =>
            _watched.ToDictionary(name => name, session.GetProperty, StringComparer.Ordinal);

        private List<PropertyChange> Changes(Dictionary<string, string> before, Dictionary<string, string> after) =>
            after
                .Where(p => before[p.Key] != p.Value)
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new PropertyChange(p.Key, Shown(p.Key, before[p.Key]), Shown(p.Key, p.Value)))
                .ToList();

        private string Shown(string name, string value) =>
            value.Length > 0 && _hidden.Contains(name) ? Masked
            : value.Length > ShownCharacters ? $"{value[..ShownCharacters]}… ({value.Length} characters)"
            : value;

        private HashSet<string> HiddenProperties() =>
            session.GetProperty("MsiHiddenProperties")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);

        private static EventReport Report(ControlEventRow row, string outcome) =>
            new(row.Event, row.Argument, row.Condition, outcome);

        private static HashSet<string> Watched(DriveTables tables, DriveRequest request)
        {
            var names = new HashSet<string>(tables.PropertyNames, StringComparer.Ordinal) { "Installed" };
            names.UnionWith(request.Properties?.Keys ?? []);
            names.UnionWith(request.Steps.OfType<SetStep>().SelectMany(s => s.Values.Keys));
            names.UnionWith(request.ActionResults?.Values.SelectMany(v => v.Keys) ?? []);
            return names;
        }
    }
}
