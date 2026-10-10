# advanced-installer-mcp

An [MCP](https://modelcontextprotocol.io) server that helps AI coding agents work with
[Advanced Installer](https://www.advancedinstaller.com) projects: lint and render setup dialogs, run builds and
return structured results.

Status: early development. See [ADR 0001](docs/adr/0001-tech-stack-and-v0.1-scope.md) for the planned v0.1 scope.

## Requirements

- Windows
- .NET 10 SDK
- Advanced Installer (for the `build` tool)

## Build and register with Claude Code

```
dotnet build
claude mcp add advinst -- "<repo>\src\AdvancedInstallerMcp\bin\Debug\net10.0-windows\AdvancedInstallerMcp.exe"
```

Point the client at the built executable, not at `dotnet run`: build output on stdout would corrupt the stdio
protocol.

## License

MIT. This project is not affiliated with Caphyon or Advanced Installer.
