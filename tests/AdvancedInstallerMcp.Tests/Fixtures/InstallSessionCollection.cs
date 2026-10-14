namespace AdvancedInstallerMcp.Tests.Fixtures;

// Windows Installer allows one install session per process: a second MsiOpenPackage while one is open fails with
// 1618, even for a restricted session. Every test class that opens one joins this collection, so they never run in
// parallel with each other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InstallSessionCollection
{
    public const string Name = "Windows Installer session";
}
