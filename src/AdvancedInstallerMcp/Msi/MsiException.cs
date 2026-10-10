using System.ComponentModel;

namespace AdvancedInstallerMcp.Msi;

public sealed class MsiException(uint errorCode, string operation)
    : Exception($"{operation} failed: {new Win32Exception((int)errorCode).Message} (error {errorCode}).")
{
    public uint ErrorCode { get; } = errorCode;

    internal static void ThrowIfFailed(uint errorCode, string operation)
    {
        if (errorCode != NativeMethods.ErrorSuccess)
        {
            throw new MsiException(errorCode, operation);
        }
    }
}
