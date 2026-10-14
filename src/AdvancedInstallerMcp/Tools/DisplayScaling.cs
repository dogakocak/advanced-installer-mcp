using ModelContextProtocol;

namespace AdvancedInstallerMcp.Tools;

// The display scalings the picture tools draw at, as Windows names them, and their DPI.
internal static class DisplayScaling
{
    private static readonly Dictionary<int, int> DpiByScaling = new() { [100] = 96, [125] = 120, [150] = 144 };

    public static int ToDpi(int scaling) =>
        DpiByScaling.TryGetValue(scaling, out var dpi) ? dpi : throw new McpException("scaling must be 100, 125 or 150.");
}
