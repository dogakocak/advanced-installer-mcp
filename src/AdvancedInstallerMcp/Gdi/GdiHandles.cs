using System.Runtime.InteropServices;

namespace AdvancedInstallerMcp.Gdi;

// A memory device context, deleted exactly once.
internal sealed class DeviceContextHandle : SafeHandle
{
    public DeviceContextHandle() : base(0, ownsHandle: true)
    {
    }

    public static DeviceContextHandle CreateMemory()
    {
        var handle = new DeviceContextHandle();
        handle.SetHandle(NativeMethods.CreateCompatibleDC(0));
        return handle.IsInvalid ? throw new InvalidOperationException("CreateCompatibleDC failed.") : handle;
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle() => NativeMethods.DeleteDC(handle);
}

// An HFONT, deleted exactly once. It must not be selected into a DC when that happens, so callers select the
// previous font back first.
internal sealed class FontHandle : SafeHandle
{
    public FontHandle() : base(0, ownsHandle: true)
    {
    }

    public static FontHandle Own(nint font)
    {
        var handle = new FontHandle();
        handle.SetHandle(font);
        return handle.IsInvalid ? throw new InvalidOperationException("CreateFont failed.") : handle;
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle() => NativeMethods.DeleteObject(handle);
}
