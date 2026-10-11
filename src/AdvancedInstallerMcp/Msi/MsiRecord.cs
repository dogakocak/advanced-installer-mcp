namespace AdvancedInstallerMcp.Msi;

// One fetched row. Valid only inside the map callback of MsiDatabase.Query; fields are 1-based like in msi.dll.
public sealed class MsiRecord
{
    private readonly MsiHandle _handle;

    internal MsiRecord(MsiHandle handle) => _handle = handle;

    public bool IsNull(int field) => NativeMethods.MsiRecordIsNull(_handle, (uint)field);

    public int? GetInteger(int field)
    {
        var value = NativeMethods.MsiRecordGetInteger(_handle, (uint)field);
        return value == NativeMethods.NullInteger ? null : value;
    }

    // The whole content of a stream field (a Binary row's Data, for example).
    public byte[] ReadStream(int field)
    {
        using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var length = (uint)buffer.Length;
            MsiException.ThrowIfFailed(
                NativeMethods.MsiRecordReadStream(_handle, (uint)field, buffer, ref length), $"Reading stream field {field}");
            if (length == 0)
            {
                return content.ToArray();
            }

            content.Write(buffer, 0, (int)length);
        }
    }

    // Empty string for a null field, as msi.dll returns it; check IsNull when the difference matters.
    public string GetString(int field)
    {
        var buffer = new char[256];
        var length = (uint)buffer.Length;
        var result = NativeMethods.MsiRecordGetString(_handle, (uint)field, buffer, ref length);

        if (result == NativeMethods.ErrorMoreData)
        {
            buffer = new char[length + 1];
            length = (uint)buffer.Length;
            result = NativeMethods.MsiRecordGetString(_handle, (uint)field, buffer, ref length);
        }

        MsiException.ThrowIfFailed(result, $"Reading field {field}");
        return new string(buffer, 0, (int)length);
    }
}
