using AdvancedInstallerMcp.Build;

namespace AdvancedInstallerMcp.Tests.Build;

public sealed class AipBuildsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("advinst-mcp-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string WriteProject(params string[] buildRows)
    {
        var path = Path.Combine(_folder, "Sample.aip");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
            "<DOCUMENT Type=\"Advanced Installer\" Version=\"2\">\r\n" +
            "  <COMPONENT cid=\"caphyon.advinst.msicomp.BuildComponent\">\r\n" +
            string.Concat(buildRows.Select(row => $"    {row}\r\n")) +
            "  </COMPONENT>\r\n</DOCUMENT>\r\n");
        return path;
    }

    private const string DefaultRow =
        "<ROW BuildKey=\"DefaultBuild\" BuildName=\"DefaultBuild\" BuildOrder=\"1\" BuildType=\"0\" " +
        "PackageFolder=\"[|AI_AIP_FOLDER][|AI_AIP_FILENAME]-SetupFiles\" PackageFileName=\"[|AI_AIP_FILENAME]\"/>";

    [Fact]
    public void Resolves_the_package_of_an_up_to_date_build()
    {
        var project = WriteProject(DefaultRow);
        var package = Path.Combine(_folder, "Sample-SetupFiles", "Sample.msi");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        File.WriteAllText(package, "");

        AipBuilds.Read(project).ShouldBe([new BuildBlock("DefaultBuild", package)]);
    }

    [Fact]
    public void Finds_an_exe_package_too()
    {
        var project = WriteProject(DefaultRow);
        var package = Path.Combine(_folder, "Sample-SetupFiles", "Sample.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        File.WriteAllText(package, "");

        AipBuilds.Read(project).Single().Package.ShouldBe(package);
    }

    [Fact]
    public void Package_is_null_when_the_file_is_not_there()
    {
        AipBuilds.Read(WriteProject(DefaultRow)).ShouldBe([new BuildBlock("DefaultBuild", null)]);
    }

    [Fact]
    public void Other_path_variables_are_not_resolved()
    {
        var project = WriteProject(
            "<ROW BuildKey=\"Out\" BuildName=\"Out\" BuildOrder=\"1\" PackageFolder=\"[|OUTPUT_DIR]\" PackageFileName=\"Setup\"/>");

        AipBuilds.Read(project).ShouldBe([new BuildBlock("Out", null)]);
    }

    [Fact]
    public void Keeps_the_build_order()
    {
        var project = WriteProject(
            DefaultRow.Replace("DefaultBuild", "Second").Replace("BuildOrder=\"1\"", "BuildOrder=\"2\""),
            DefaultRow);

        AipBuilds.Read(project).Select(b => b.Name).ShouldBe(["DefaultBuild", "Second"]);
    }
}
