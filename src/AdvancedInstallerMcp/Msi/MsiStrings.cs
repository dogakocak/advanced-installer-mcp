namespace AdvancedInstallerMcp.Msi;

// msi.dll returns strings into a caller buffer: length is the buffer size in characters on input and the string
// length without the terminating null on output; ERROR_MORE_DATA means "call again with length + 1".
internal static class MsiStrings
{
    public delegate uint Reader(char[] buffer, ref uint length);

    public static string Read(Reader read, string operation)
    {
        var buffer = new char[256];
        var length = (uint)buffer.Length;
        var result = read(buffer, ref length);

        if (result == NativeMethods.ErrorMoreData)
        {
            buffer = new char[length + 1];
            length = (uint)buffer.Length;
            result = read(buffer, ref length);
        }

        MsiException.ThrowIfFailed(result, operation);
        return new string(buffer, 0, (int)length);
    }
}
