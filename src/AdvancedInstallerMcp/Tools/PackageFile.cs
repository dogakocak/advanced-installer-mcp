using AdvancedInstallerMcp.Msi;
using ModelContextProtocol;

namespace AdvancedInstallerMcp.Tools;

// Opens the built .msi a dialog tool was pointed at, with an explanation the caller can act on when it cannot.
internal static class PackageFile
{
    public static MsiDatabase Open(string msiPath, string toolName)
    {
        if (!File.Exists(msiPath))
        {
            throw new McpException($"File not found: {msiPath}");
        }

        if (string.Equals(Path.GetExtension(msiPath), ".aip", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"{toolName} reads the built .msi, not the .aip project. Build the project first.");
        }

        try
        {
            return MsiDatabase.OpenReadOnly(msiPath);
        }
        catch (MsiException e)
        {
            throw new McpException($"Not a readable MSI package: {msiPath} ({e.Message})");
        }
    }
}
