using System.Runtime.InteropServices;

namespace AdvancedInstallerMcp.Msi;

// Owns one MSIHANDLE and closes it exactly once, even if Dispose is forgotten (the finalizer runs ReleaseHandle).
internal sealed class MsiHandle : SafeHandle
{
    // Passed where a function accepts "no record" (0); never closed.
    public static readonly MsiHandle None = new(0, ownsHandle: false);

    public MsiHandle() : base(0, ownsHandle: true)
    {
    }

    private MsiHandle(uint handle, bool ownsHandle) : base(0, ownsHandle) => SetHandle((nint)handle);

    public static MsiHandle Own(uint handle) => new(handle, ownsHandle: true);

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle() => NativeMethods.MsiCloseHandle((uint)handle) == NativeMethods.ErrorSuccess;
}
