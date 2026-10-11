using System.Text;
using AdvancedInstallerMcp.Build;

namespace AdvancedInstallerMcp.Tests.Build;

// Real AdvancedInstaller.com output, captured as described in Fixtures/BuildOutput/README.md.
public sealed class BuildOutputParserTests
{
    private static BuildOutput Parse(string fixture) =>
        BuildOutputParser.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "BuildOutput", fixture), Encoding.UTF8));

    [Fact]
    public void Success_gives_the_build_and_its_package()
    {
        var output = Parse("success.txt");

        output.Builds.ShouldBe([new BuildBlock("DefaultBuild", @"C:\Work\Sample-SetupFiles\Sample.msi")]);
        output.FinishedSuccessfully.ShouldBeTrue();
        output.UpToDate.ShouldBeFalse();
        output.Validation.ShouldBeEmpty();
        output.Errors.ShouldBeEmpty();
        output.Unrecognized.ShouldBeEmpty();
    }

    [Fact]
    public void Two_builds_keep_their_validation_notes_apart_from_errors()
    {
        var output = Parse("two-builds-ai-ice.txt");

        output.Builds.ShouldBe([
            new BuildBlock("DefaultBuild", @"C:\Work\Sample-SetupFiles\Sample.msi"),
            new BuildBlock("Brand2", @"C:\Work\Sample-Brand2\Sample.msi"),
        ]);
        output.Validation.Select(n => (n.Build, n.Code, n.Severity)).ShouldBe([
            ("DefaultBuild", "AI_ICE05", "ERROR"),
            ("Brand2", "AI_ICE05", "ERROR"),
        ]);
        output.Validation[0].Message.ShouldBe(
            "The custom action 'UiOnlySet' was scheduled only in 'InstallUISequence' table. " +
            "It will not be executed during an un-attended installation.");
        output.Errors.ShouldBeEmpty();
        output.Unrecognized.ShouldBeEmpty();
        output.FinishedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void Only_the_builds_that_ran_have_a_block()
    {
        var output = Parse("one-of-two-up-to-date.txt");

        output.Builds.ShouldBe([new BuildBlock("Brand2", @"C:\Work\Sample-Brand2\Sample.msi")]);
        output.UpToDate.ShouldBeFalse();
        output.FinishedSuccessfully.ShouldBeTrue();
        output.Unrecognized.ShouldBeEmpty();
    }

    [Fact]
    public void Error_block_ends_at_the_finish_line_too()
    {
        BuildOutputParser.Parse("Error: Something broke: \r\nC:\\a.txt \r\nBuild finished because an error was encountered.\r\n")
            .Errors.ShouldBe([@"Something broke: C:\a.txt"]);
    }

    [Fact]
    public void Up_to_date_has_no_builds_to_report()
    {
        var output = Parse("up-to-date.txt");

        output.UpToDate.ShouldBeTrue();
        output.FinishedSuccessfully.ShouldBeTrue();
        output.Builds.ShouldBeEmpty();
        output.Unrecognized.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_resource_joins_the_path_lines_to_the_error()
    {
        var output = Parse("missing-resource.txt");

        output.Errors.ShouldBe([@"Resources referred by the project are missing: C:\Work\hello.txt"]);
        output.FinishedSuccessfully.ShouldBeFalse();
        output.Unrecognized.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_build_is_an_error_without_a_prefix()
    {
        var output = Parse("unknown-build.txt");

        output.Errors.ShouldBe(["Build not found: \"NoSuchBuild\"."]);
        output.FinishedSuccessfully.ShouldBeFalse();
    }

    [Fact]
    public void Missing_project_is_an_error()
    {
        Parse("project-not-found.txt").Errors.ShouldBe([@"File not found: ""C:\Work\NoSuch.aip""."]);
    }

    [Fact]
    public void Non_ascii_paths_survive()
    {
        Parse("non-ascii-path.txt").Builds.Single().Package.ShouldBe(@"C:\Work\Çıktı Ş\Örnek-SetupFiles\Örnek.msi");
    }

    [Fact]
    public void Lines_it_does_not_know_are_kept()
    {
        var output = BuildOutputParser.Parse(
            "Checking builds status \r\nBuild required. \r\n[ DefaultBuild ] \r\nSigning package \r\n" +
            "WARNING: ICE57. Component 'X' has both per-user and per-machine data. \r\nBuild finished successfully.\r\n");

        output.Unrecognized.ShouldBe(["Signing package"]);
        output.Validation.Single().ShouldBe(new ValidationNote(
            "DefaultBuild", "ICE57", "WARNING", "Component 'X' has both per-user and per-machine data."));
    }

    [Fact]
    public void Error_block_ends_at_the_first_blank_line()
    {
        var output = BuildOutputParser.Parse(
            "Error: Resources referred by the project are missing: \r\nC:\\a.txt \r\nC:\\b.txt \r\n \r\nSomething else \r\n");

        output.Errors.ShouldBe([@"Resources referred by the project are missing: C:\a.txt, C:\b.txt"]);
        output.Unrecognized.ShouldBe(["Something else"]);
    }
}
