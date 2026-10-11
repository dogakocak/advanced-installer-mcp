using System.Diagnostics;
using AdvancedInstallerMcp.Build;

namespace AdvancedInstallerMcp.Tests.Build;

public sealed class AdvancedInstallerCliTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("advinst-mcp-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FakeCom(string folder)
    {
        var path = Path.Combine(_folder, folder, "bin", "x86", "AdvancedInstaller.com");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void ADVINST_COM_wins_and_loses_its_quotes()
    {
        var com = FakeCom("Advanced Installer 24.0");

        AdvancedInstallerCli.Locate($"\"{com}\"", _folder).ShouldBe(com);
    }

    [Fact]
    public void Falls_back_to_the_highest_installed_version()
    {
        FakeCom("Advanced Installer 9.8");
        var newest = FakeCom("Advanced Installer 24.0");
        FakeCom("Advanced Installer 23.5");

        AdvancedInstallerCli.Locate(null, _folder).ShouldBe(newest);
        AdvancedInstallerCli.Locate(@"C:\gone\AdvancedInstaller.com", _folder).ShouldBe(newest);
    }

    [Fact]
    public void Nothing_installed_is_null()
    {
        AdvancedInstallerCli.Locate(null, _folder).ShouldBeNull();
    }

    [Fact]
    public async Task Reads_utf8_output_and_the_exit_code()
    {
        var run = await AdvancedInstallerCli.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-Command",
             "[Console]::OutputEncoding = [Text.Encoding]::UTF8; [Console]::Out.Write('Çıktı Ş'); exit 3"],
            TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        run.ExitCode.ShouldBe(3);
        run.StandardOutput.ShouldBe("Çıktı Ş");
        run.TimedOut.ShouldBeFalse();
    }

    // AdvancedInstaller.com starts advinst.exe, which does the build; a timeout must stop both.
    [Fact]
    public async Task Timeout_kills_the_child_processes_too()
    {
        var pidFile = Path.Combine(_folder, "child.pid");
        var script =
            "$c = Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep 60' -PassThru -WindowStyle Hidden; " +
            $"Set-Content -LiteralPath '{pidFile}' $c.Id; Start-Sleep 60";

        var run = await AdvancedInstallerCli.RunAsync(
            "powershell.exe", ["-NoProfile", "-Command", script], TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        run.TimedOut.ShouldBeTrue();
        var child = int.Parse(File.ReadAllText(pidFile).Trim());
        IsRunning(child).ShouldBeFalse();
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
