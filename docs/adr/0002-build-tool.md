# ADR 0002 — build: parameters, process handling and result schema

- Date: 2026-10-12
- Status: Accepted

## Context

ADR 0001 puts `build` in v0.1: run `AdvancedInstaller.com /build <aip> [-buildslist …]` and return a structured
result, with `AI_ICE*` noise kept apart from real errors. It left open the parameters, the result schema and how
warnings are classified.

What the documentation says (Advanced Installer 24.0 help, "Working with Advanced Installer from the Command Line"
and "Command line return codes"):

- `AdvancedInstaller.com /build <project> [-buildslist a;b]` builds the named builds (all of them by default).
  `/rebuild` does the same "regardless whether the project was modified and without using any cached files".
  `-configurationslist` is for WinCE projects only.
- Since 22.0 the installer sets the `ADVINST_COM` environment variable to the newest installed
  `AdvancedInstaller.com`.
- Return codes: `0` success, `0xE0010064` `AI_ERROR_EXCEPTION` ("an exception occurs and no other error code is
  specified"), `0xE0010068` file not found, `0xE0010069` malformed command line, `0xE001006C` no license / trial
  expired, `0xE001006D` no floating license, and a few edit-command codes.
- `AI_ICE01`–`AI_ICE07` are Caphyon's own best-practice validators (SelfReg, TypeLib, script custom actions, custom
  action only in the UI sequence, non-public or default-less dialog properties).

What a scratch project showed (`/newproject`, `/edit /AddFile`, builds; outputs in `tests/Fixtures/BuildOutput`):

- Success prints `[ BuildName ]`, then `Building package: <full path>`, a fixed list of progress lines, and
  `Build finished successfully.`. Two builds print one block each.
- `ERROR: AI_ICE05. …` is printed under `Validating MSI` and the build still succeeds with exit code `0`. The
  baseline project printed six such lines on every clean build.
- A second `/build` with nothing changed prints `All builds are up to date.`: **no package path at all**.
- Failures have no common prefix. A missing source file prints `Error: Resources referred by the project are
  missing:` followed by bare path lines; an unknown build prints `Build not found: "X".`. Both exit with
  `0xE0010064`, so only the text tells them apart. A missing `.aip` prints `File not found: "…".` and exits with
  `0xFFFFFFFF`, which is not in the documented table.
- Every line ends with a space. Standard error stayed empty in every run.
- Standard output is UTF-8 when redirected: a path with `Çıktı Ş` decoded correctly as UTF-8 and as mojibake with
  the OEM code page.
- In the baseline runs the build ran attached to a console and long lines came back wrapped at the console width
  (`It will not be` / `executed during …`). Redirected without a console they arrive whole.
- `AdvancedInstaller.com` is a launcher: during a build it starts `advinst.exe /rebuild "<project>"` as a child
  process, which does the work.
- `/build` did not change the `.aip` (same hash before and after), so the CRLF pitfall of editing `.aip` files does
  not apply to this tool.
- The value of `ADVINST_COM` on this machine includes the surrounding double quotes.

## Decision

### Parameters

| Parameter | Maps to | Notes |
|---|---|---|
| `aipPath` (required) | `<project>` | Must exist and end in `.aip`; checked before the CLI runs, so the undocumented `0xFFFFFFFF` never reaches the caller |
| `builds` (optional list) | `-buildslist a;b` | Omitted = every build, like the CLI. A name with `;` is rejected |
| `rebuild` (default `false`) | `/rebuild` instead of `/build` | |
| `timeoutMinutes` (default 30) | | Baseline builds of a two-build product took one to two minutes; 30 leaves room for large packages and signing |

Out of scope: setting the version (that is `/edit /SetVersion`, a change to the `.aip`, so it belongs with the v0.2
editing tools), `-configurationslist` (WinCE), and the optional project verification script that ADR 0001 mentioned
(see below).

### Running the CLI

- Executable: `ADVINST_COM` with surrounding quotes removed; if unset or the file is missing, the highest version
  under `%ProgramFiles(x86)%\Caphyon\Advanced Installer *\bin\x86\AdvancedInstaller.com`; otherwise an error that says
  Advanced Installer was not found.
- `UseShellExecute = false`, `CreateNoWindow = true`, arguments through `ArgumentList` (no hand-built quoting),
  standard output and standard error redirected and decoded as UTF-8. No console is a correctness rule: with one,
  long lines wrap and the parser would see half messages.
- One build at a time per server process (a `SemaphoreSlim`). Whether two builds of one project can run side by side
  is untested, and a caller gains little from trying.
- On timeout or when the client cancels the call, the whole process tree is killed
  (`Process.Kill(entireProcessTree: true)`): killing only `AdvancedInstaller.com` would leave `advinst.exe` building
  and holding the output files.

### Reading the output

Lines are trimmed at the end (trailing space, CR). The parser knows a **closed list of progress lines** (`Checking
builds status`, `Build required.`, `Prepare build`, `Detecting MSI incompatible resources`, `Preparing files`,
`Reusing archives from cache`, `Creating CAB file(s)`, `Preparing binaries`, `Creating MSI database`, `Inserting CAB
file(s) into MSI`, `Writing Summary Information`, `Validating MSI`, `Build time: …`, `Total build time: …`, `Build
finished …`, blank lines) and drops only those. Everything else is kept and sorted:

- `[ Name ]` starts a build; `Building package: <path>` gives its package.
- `ERROR:` or `WARNING:` followed by `AI_ICE` or `ICE` and a number is a **validation note**: code, the printed
  severity, message, and the build it belongs to. Notes never make the result `failed`; the exit code and the
  failure lines decide that.
- `Error: …` and the non-blank lines after it, `Build not found: …`, `File not found: …` are **errors**.
- Any other line goes to `unrecognized`, so a message from a newer Advanced Installer or from a case we have not
  seen is never lost.

### Result

```json
{
  "project": "C:\\Work\\Sample.aip",
  "status": "succeeded | upToDate | failed",
  "exitCode": "0x00000000",
  "exitCodeName": "AI_NO_ERROR | AI_ERROR_EXCEPTION | … | undocumented",
  "builds": [ { "name": "DefaultBuild", "package": "C:\\Work\\Sample-SetupFiles\\Sample.msi", "buildTime": "1 sec." } ],
  "validation": [ { "build": "DefaultBuild", "code": "AI_ICE05", "severity": "ERROR", "message": "The custom action …" } ],
  "errors": [ "Resources referred by the project are missing: C:\\Work\\hello.txt" ],
  "unrecognized": [],
  "timedOut": false
}
```

`status` is `failed` when the exit code is not 0 or the last line is not `Build finished successfully.`. The full
output is not returned; an agent asked for a structured result, and the baseline shows raw logs cost context.
When `unrecognized` is not empty, those lines are returned as they are, capped at 50.

**Up to date has no package path.** In that case `builds` is filled from the `BuildComponent` rows of the `.aip`
(the build names, or only the requested ones). `package` is resolved only when `PackageFolder` and
`PackageFileName` use no variables other than `[|AI_AIP_FOLDER]` and `[|AI_AIP_FILENAME]` and a `.msi` or `.exe`
with that name exists; otherwise it is `null` and the message suggests `rebuild: true`. General path-variable
resolution is out. An agent that has just changed the project always gets `Build required`, so this case mostly
happens on a repeated call, when the caller already knows the path.

### Annotations

`ReadOnly = false`, `Destructive = false` (it writes only the build output folder, which the project defines),
`Idempotent = false`, `OpenWorld = false`.

## Alternatives considered

- **Return the raw output and let the agent read it.** That is the baseline: the agent filters the same lines every
  time and has to know that `ERROR: AI_ICE` is not an error.
- **Treat exit code 0 as the whole truth.** The exit code cannot tell a missing file from an unknown build name, and
  a successful build can still carry validation notes the caller should see.
- **Run the project's verification script from `build` (ADR 0001).** The only thing the tool would add over the
  agent's own shell is capturing every PowerShell stream correctly, and running a script named in a tool argument
  is a much wider permission than "build this project". Dropped from v0.1; ADR 0001 is amended. A dedicated tool
  that checks the built MSI's tables (files, components, features) is the better answer to "exit 0 does not mean
  the package is right" and can come later.
- **Parse `.aip` path variables to always return package paths.** Path variables can come from the global
  Advanced Installer settings and from files loaded with `/loadpathvars`; the cost is out of proportion to a case
  that `rebuild: true` solves.

## Consequences

- The parser is tested without Advanced Installer, against the captured outputs in `tests/Fixtures/BuildOutput`
  (success, up to date, missing resource, unknown build, two builds with `AI_ICE05`, non-ASCII path, missing
  project). An opt-in test behind `ADVINST_MCP_REAL_AIP` runs a real build, like `ADVINST_MCP_REAL_MSI` for the MSI
  reader.
- The returned `package` path is what `lint_dialog` takes, so "build, then lint" needs no path guessing.
- License failures (`0xE001006C`, `0xE001006D`) are reported by name, so the caller can tell "fix the project" from
  "fix the machine".

## Open points

- What a failing Microsoft ICE (for example ICE43, ICE57) prints, and whether it fails the build, was not seen. The
  parser already treats `ICE` lines as validation notes; the build's exit code decides `failed`.
- Output of digital signing, prerequisites and EXE bootstrapper builds was not captured. New lines will show up in
  `unrecognized` first and become known lines when seen.
