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

    [LibraryImport(Msi)]
    public static partial uint MsiCloseHandle(uint handle);
}
