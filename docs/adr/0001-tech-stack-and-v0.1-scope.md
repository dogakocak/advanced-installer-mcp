# ADR 0001 — Tech stack and v0.1 scope

- Date: 2026-10-11
- Status: Accepted

## Context

Before writing any code we measured how an AI coding agent (Claude Code, no MCP server) handles Advanced Installer
work on a real-world project: one `.aip` with two builds, two Windows services, custom PowerShell actions and custom
setup dialogs. Six task types, three runs each (18 runs), all elevated:

| Task | Result | Typical cost |
|---|---|---|
| Bump version / add a file / service recovery / fix a broken build / end-to-end install | all passed, no human intervention | 1–13 min, 0.2–6.6M cache-read tokens |
| Setup UI: add a checkbox to a dialog, gate Next on a successful connection test, no clipped text | all passed, no human intervention | **20–30 min, 12–22M cache-read tokens** |

What we learned:

- Success rate is already at the ceiling. An MCP server will not be judged on "can it do it" but on **cost,
  variance and safety**.
- The UI task was the most expensive one by far. Every run rebuilt the same machinery from scratch to *see* the
  dialog: build the MSI, start the real installer, drive it through Win32 messages (UI Automation exposes MSI
  controls only as plain panes; `WM_SETTEXT` does not commit the property until focus is lost), capture it with
  `PrintWindow` (which sometimes drops labels). A clipped label was found only through that loop.
- Build output is free text; `AI_ICE*` lines print as `ERROR` even on a clean build; PowerShell warnings get lost
  when redirected.
- Text-editing `.aip` files from Git Bash (`sed -i`) broke CRLF line endings twice.
- A deferred custom action without the HideTarget flag wrote a password into the verbose MSI log. Log handling is a
  safety concern for any install tool.

## Decision

### v0.1 scope: three tools

| Tool | What it does | Side effects |
|---|---|---|
| `lint_dialog` | Reads one dialog's `Dialog` / `Control` / `ControlEvent` / `ControlCondition` rows from an MSI and checks bounds, overlaps, the tab chain and whether every static text fits, **measured with the real font** | none (read-only) |
| `render_dialog` | Draws an approximate PNG of the dialog from the same rows, without installing anything | temporary PNG |
| `build` | Runs `AdvancedInstaller.com /build <aip> [-buildslist …]` and returns a structured result: MSI paths, errors, and `AI_ICE*` noise kept apart. An optional project-specific verification script can be passed in | writes the build output folder |

Deferred to v0.2: `install` / `uninstall` (the server owns the log path and masks secrets; it changes the system,
so it needs its own security design) and `.aip` editing tools (`add_file`, `set_service_recovery`).

### Stack

- **Language / runtime:** C# on .NET 10, target `net10.0-windows`. Font measurement and drawing need Windows GDI
  (`TextRenderer` / `System.Drawing`); the MSI API is Windows-only anyway.
- **MCP SDK:** the official `ModelContextProtocol` package (2.2.0 at the time of writing) with
  `Microsoft.Extensions.Hosting`. Pattern: `Host.CreateApplicationBuilder` →
  `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`; tools are `[McpServerTool]` methods on
  `[McpServerToolType]` classes.
- **Transport:** stdio. The client starts the server as a child process; no port, no authentication. Rule: stdout
  carries protocol messages only, **all logging goes to stderr** (`LogToStandardErrorThreshold = Trace`). A single
  stray `Console.WriteLine` breaks the connection.
- **Reading MSI files:** a thin P/Invoke wrapper of our own over `msi.dll` (`MsiOpenDatabase`,
  `MsiDatabaseOpenView`, `MsiViewExecute`, `MsiViewFetch`, `MsiRecordGetString`, `MsiCloseHandle`, wrapped in
  `SafeHandle`).
- **Tests:** xUnit + Shouldly. Fixtures are ~~built from a small sample `.aip` in this repo~~ small MSI packages
  that the tests create through `msi.dll` itself, with a deliberately clipped label and an overlapping control,
  plus a fixed copy, so every check is tested both ways. *Amended 2026-10-11:* Advanced Installer's command line
  has no commands for dialogs or controls, so a sample `.aip` could only be made in the GUI or by hand-editing XML;
  generated packages need no Advanced Installer and run in CI. An opt-in test (`ADVINST_MCP_REAL_MSI`) checks the
  reader against a real package.
- **License:** MIT. The project is not affiliated with Caphyon or Advanced Installer.

## Alternatives considered

- **Python or TypeScript SDK:** more examples, but MSI and GDI access would still go through P/Invoke or COM, and
  the target environment (Windows, MSI, .NET shops) is closest to C#.
- **`WixToolset.Dtf.WindowsInstaller`:** a mature managed MSI wrapper, but recent WiX releases come with usage terms
  that would need a separate review, and we only need six functions.
- **COM (`WindowsInstaller.Installer`):** short with `dynamic`, but untyped; mistakes surface only at run time.
- **HTTP transport:** adds authentication and port management with no benefit for a single local user.

## Consequences

- The same measurement is repeated with the server enabled. Expected gains are in time, tokens and run-to-run
  variance, not in success rate.
- `render_dialog` is approximate (no theme bitmaps, no real window chrome); the real screen remains the final judge.
  Because text is measured with the real font, clipping detection should beat a characters-times-width heuristic.
  First check: an 80-character hint in a 230×18 DLU text control must be reported as clipped, a 24-character
  checkbox label in 125×12 DLU must fit.
- `net10.0-windows` means Windows only, which matches the target.

## Open questions

- ADR 0002: `build` parameters and result schema (build names, version, warning classification).
- ADR 0003: DLU → pixel conversion and font selection (Tahoma 8 vs. theme font), shared by `lint_dialog` and
  `render_dialog`.
- ADR 0004 (v0.2): security model of `install` (secrets never travel as tool arguments; log masking).
- ADR 0005: `lint_dialog` layout rules (bounds, overlaps, tab chain, embedded UI).
