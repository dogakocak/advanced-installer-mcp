namespace AdvancedInstallerMcp.Msi;

public enum ActionOutcome
{
    Ran,

    // Unknown action, or one the restricted engine refuses (custom actions that run package code).
    NotRun,

    Failed,
    Cancelled,
}

// An install session on a package, opened without installing (ADR 0007): Windows Installer's own engine for
// properties, conditions and formatting. Opened from outside msiexec it is a restricted engine, so DoAction runs
// built-in and property-setting actions only; the package's code never runs.
public sealed class MsiSession : IDisposable
{
    private const uint ErrorInstallUserExit = 1602;
    private const uint ErrorInstallFailure = 1603;

    private readonly MsiHandle _handle;

    private MsiSession(MsiHandle handle) => _handle = handle;

    public static MsiSession Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MSI package not found.", path);
        }

        // Nothing the engine might show (an error box from an action) may open a window in a server process.
        NativeMethods.MsiSetInternalUI(NativeMethods.UiLevelNone, 0);
        MsiException.ThrowIfFailed(
            NativeMethods.MsiOpenPackageEx(path, NativeMethods.OpenPackageIgnoreMachineState, out var handle),
            $"Opening a session on \"{path}\"");
        return new MsiSession(MsiHandle.Own(handle));
    }

    public string GetProperty(string name) =>
        MsiStrings.Read(
            (char[] buffer, ref uint length) => NativeMethods.MsiGetProperty(_handle, name, buffer, ref length),
            $"Reading property {name}");

    // An empty value deletes the property, as everywhere in Windows Installer.
    public void SetProperty(string name, string value) =>
        MsiException.ThrowIfFailed(NativeMethods.MsiSetProperty(_handle, name, value), $"Setting property {name}");

    // True for an empty condition (Windows Installer treats "no condition" as true); null when it does not parse.
    public bool? EvaluateCondition(string condition) =>
        NativeMethods.MsiEvaluateCondition(_handle, condition) switch
        {
            NativeMethods.ConditionTrue or NativeMethods.ConditionNone => true,
            NativeMethods.ConditionFalse => false,
            _ => null,
        };

    // Resolves [Property] references (and the other formatted-text forms) as the engine does.
    public string Format(string text)
    {
        using var record = MsiHandle.Own(NativeMethods.MsiCreateRecord(0));
        MsiException.ThrowIfFailed(NativeMethods.MsiRecordSetString(record, 0, text), "Formatting");
        return MsiStrings.Read(
            (char[] buffer, ref uint length) => NativeMethods.MsiFormatRecord(_handle, record, buffer, ref length),
            "Formatting");
    }

    public ActionOutcome DoAction(string action)
    {
        var result = NativeMethods.MsiDoAction(_handle, action);
        return result switch
        {
            NativeMethods.ErrorSuccess => ActionOutcome.Ran,
            NativeMethods.ErrorFunctionNotCalled => ActionOutcome.NotRun,
            ErrorInstallFailure => ActionOutcome.Failed,
            ErrorInstallUserExit => ActionOutcome.Cancelled,
            _ => throw new MsiException(result, $"Running action {action}"),
        };
    }

    public void Dispose() => _handle.Dispose();
}
