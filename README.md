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

Publish to a folder of its own and point the client at that executable:

```
dotnet publish src/AdvancedInstallerMcp -c Release -o "%LOCALAPPDATA%\advinst-mcp"
claude mcp add advinst -- "%LOCALAPPDATA%\advinst-mcp\AdvancedInstallerMcp.exe"
```

- Not `bin\Debug`: the client keeps the server running for the whole session, which locks the file and breaks the
  next `dotnet build`. Publish again (with the session closed) to update the registered server.
- Not `dotnet run`: build output on stdout would corrupt the stdio protocol.

## Tools

| Tool | What it does |
|---|---|
| `lint_dialog` | Reads the setup dialogs of a built `.msi` (no install) and reports labels of Text, CheckBox and PushButton controls that do not fit, measured with the package's own font as Windows draws it, at 100, 125 and 150 % scaling. Each finding gives the smallest size in installer units that fits. Pass `dialog` to check one dialog. |
| `ping` | Checks that the server runs. |

How the measurement works and where its numbers come from: [ADR 0003](docs/adr/0003-installer-units-and-text-measurement.md)
and the [control calibration](docs/research/2026-10-11-control-calibration.md).

## Tests

```
dotnet test
```

The tests build small MSI packages with `msi.dll` itself, so they do not need Advanced Installer.

## License

MIT. This project is not affiliated with Caphyon or Advanced Installer.
