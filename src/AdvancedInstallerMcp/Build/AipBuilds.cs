using System.Xml.Linq;

namespace AdvancedInstallerMcp.Build;

// The builds an .aip defines, for the "All builds are up to date." case where the CLI prints no package path
// (ADR 0002). Only [|AI_AIP_FOLDER] and [|AI_AIP_FILENAME] are resolved; any other path variable gives no package.
public static class AipBuilds
{
    private const string BuildComponent = "caphyon.advinst.msicomp.BuildComponent";
    private static readonly string[] PackageExtensions = [".msi", ".exe"];

    public static IReadOnlyList<BuildBlock> Read(string aipPath)
    {
        var rows = XDocument.Load(aipPath).Root?
            .Elements("COMPONENT")
            .Where(component => (string?)component.Attribute("cid") == BuildComponent)
            .Elements("ROW") ?? [];

        return rows
            .OrderBy(row => int.TryParse((string?)row.Attribute("BuildOrder"), out var order) ? order : int.MaxValue)
            .Select(row => new BuildBlock(
                (string?)row.Attribute("BuildName") ?? (string?)row.Attribute("BuildKey") ?? "",
                FindPackage(aipPath, (string?)row.Attribute("PackageFolder"), (string?)row.Attribute("PackageFileName"))))
            .ToList();
    }

    private static string? FindPackage(string aipPath, string? folder, string? fileName)
    {
        if (folder is null || fileName is null)
        {
            return null;
        }

        var aipFolder = Path.GetDirectoryName(Path.GetFullPath(aipPath))!;
        string Resolve(string value) => value
            .Replace("[|AI_AIP_FOLDER]", aipFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .Replace("[|AI_AIP_FILENAME]", Path.GetFileNameWithoutExtension(aipPath), StringComparison.Ordinal);

        var resolvedFolder = Resolve(folder);
        var resolvedName = Resolve(fileName);
        if (resolvedFolder.Contains("[|", StringComparison.Ordinal) || resolvedName.Contains("[|", StringComparison.Ordinal))
        {
            return null;
        }

        var basePath = Path.Combine(aipFolder, resolvedFolder, resolvedName);
        return PackageExtensions.Select(extension => basePath + extension).FirstOrDefault(File.Exists);
    }
}
