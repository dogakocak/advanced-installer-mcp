using System.Runtime.InteropServices;

namespace AdvancedInstallerMcp.Msi;

// The few msi.dll functions we need (ADR 0001). MSIHANDLE is a 32-bit unsigned long on every platform, so handles
// come back as `uint` and are wrapped in MsiHandle by the caller; inputs take MsiHandle so the marshaller keeps the
// handle alive for the duration of the call.
internal static partial class NativeMethods
{
    private const string Msi = "msi.dll";

    public const uint ErrorSuccess = 0;
    public const uint ErrorMoreData = 234;
    public const uint ErrorNoMoreItems = 259;

    // szPersist is a string pointer in the signature, but these modes are passed as small integer values.
    public static readonly nint OpenReadOnly = 0;
    public static readonly nint OpenCreate = 3;

    public const int NullInteger = unchecked((int)0x80000000);

    [LibraryImport(Msi, EntryPoint = "MsiOpenDatabaseW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiOpenDatabase(string databasePath, nint persist, out uint database);

    [LibraryImport(Msi)]
    public static partial uint MsiDatabaseCommit(MsiHandle database);

    [LibraryImport(Msi, EntryPoint = "MsiDatabaseOpenViewW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiDatabaseOpenView(MsiHandle database, string query, out uint view);

    [LibraryImport(Msi)]
    public static partial uint MsiViewExecute(MsiHandle view, MsiHandle parameters);

    [LibraryImport(Msi)]
    public static partial uint MsiViewFetch(MsiHandle view, out uint record);

    [LibraryImport(Msi)]
    public static partial uint MsiViewClose(MsiHandle view);

    [LibraryImport(Msi)]
    public static partial uint MsiCreateRecord(uint fieldCount);

    [LibraryImport(Msi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MsiRecordIsNull(MsiHandle record, uint field);

    [LibraryImport(Msi)]
    public static partial int MsiRecordGetInteger(MsiHandle record, uint field);

    // length: buffer size in characters on input; on output the string length without the terminating null.
    [LibraryImport(Msi, EntryPoint = "MsiRecordGetStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiRecordGetString(MsiHandle record, uint field, [Out] char[] buffer, ref uint length);

    [LibraryImport(Msi, EntryPoint = "MsiRecordSetStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiRecordSetString(MsiHandle record, uint field, string value);

    [LibraryImport(Msi)]
    public static partial uint MsiRecordSetInteger(MsiHandle record, uint field, int value);

    // Reads the next chunk of a stream field; length is the buffer size on input, the bytes read on output (0 at the end).
    [LibraryImport(Msi)]
    public static partial uint MsiRecordReadStream(MsiHandle record, uint field, [Out] byte[] buffer, ref uint length);

    // Tests only: a stream field can be set only from a file.
    [LibraryImport(Msi, EntryPoint = "MsiRecordSetStreamW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiRecordSetStream(MsiHandle record, uint field, string filePath);

    // Install sessions (ADR 0007). A session opened from outside msiexec is a restricted engine: built-in actions and
    // property-setting custom actions run, custom actions that run package code are refused.
    public const uint ErrorFunctionNotCalled = 1626;
    public const uint OpenPackageIgnoreMachineState = 1;
    public const int UiLevelNone = 2;

    public const int ConditionFalse = 0;
    public const int ConditionTrue = 1;
    public const int ConditionNone = 2;

    [LibraryImport(Msi, EntryPoint = "MsiOpenPackageExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiOpenPackageEx(string packagePath, uint options, out uint session);

    // Process-wide: the UI level for sessions opened afterwards. Returns the previous level.
    [LibraryImport(Msi)]
    public static partial int MsiSetInternalUI(int level, nint window);

    [LibraryImport(Msi, EntryPoint = "MsiGetPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiGetProperty(MsiHandle session, string name, [Out] char[] buffer, ref uint length);

    // An empty value deletes the property.
    [LibraryImport(Msi, EntryPoint = "MsiSetPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiSetProperty(MsiHandle session, string name, string value);

    [LibraryImport(Msi, EntryPoint = "MsiEvaluateConditionW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MsiEvaluateCondition(MsiHandle session, string condition);

    // Formats field 0 of the record as a template ([Property], [#File], ...).
    [LibraryImport(Msi, EntryPoint = "MsiFormatRecordW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiFormatRecord(MsiHandle session, MsiHandle record, [Out] char[] buffer, ref uint length);

    [LibraryImport(Msi, EntryPoint = "MsiDoActionW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiDoAction(MsiHandle session, string action);

    // Tests only: the summary stream a package needs before MsiOpenPackage accepts it (template, package code, schema).
    [LibraryImport(Msi, EntryPoint = "MsiGetSummaryInformationW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiGetSummaryInformation(MsiHandle database, string? databasePath, uint updateCount, out uint summary);

    [LibraryImport(Msi, EntryPoint = "MsiSummaryInfoSetPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiSummaryInfoSetProperty(MsiHandle summary, uint property, uint dataType, int value, nint fileTime, string? text);

    [LibraryImport(Msi)]
    public static partial uint MsiSummaryInfoPersist(MsiHandle summary);

    [LibraryImport(Msi)]
    public static partial uint MsiCloseHandle(uint handle);
}
