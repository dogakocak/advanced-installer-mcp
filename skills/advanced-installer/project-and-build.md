# Project and build

## Command line (`AdvancedInstaller.com`)

- Path: the `ADVINST_COM` environment variable (Advanced Installer 22.0+). Its value **includes surrounding double
  quotes**; strip them before use.
- `/build <aip> [-buildslist A;B]` builds the named builds, all when omitted. `/rebuild` ignores the cache.
- `AdvancedInstaller.com` only launches `advinst.exe`, which does the build. Stopping a hung build means stopping the
  process tree, not just the `.com`.
- Redirected output is UTF-8. Every line ends with a space.

| Output | Meaning |
|---|---|
| `[ Name ]` then `Building package: <path>` | that build ran; the path is its package |
| `ERROR: AI_ICE05. …`, `ERROR: AI_ICE07. …` | best-practice notes (custom action only in the UI sequence, dialog property without a default). Exit code stays 0 |
| `All builds are up to date.` | nothing was built and **no package path is printed** |
| Some builds printed, others not | the missing ones were up to date; the CLI says nothing about them |
| `Error: Resources referred by the project are missing:` + path lines | a source file is gone; exit `0xE0010064` |
| `Build not found: "X".` | wrong `-buildslist` name; also `0xE0010064` |
| `File not found: "….aip".` | wrong project path; exit `0xFFFFFFFF` |

Exit codes: `0xE0010064` is the generic failure (read the text), `0xE001006C` no license or trial expired,
`0xE001006D` no floating license slot.

## Editing the `.aip`

- CRLF line endings. Check with `file x.aip` ("with CRLF line terminators"); `grep -c $'\r$'` from Git Bash can
  report 0 on a correct file.
- Prefer `AdvancedInstaller.com /edit <aip> /<Command>`, which saves the project and keeps CRLF, e.g.
  `/edit Setup.aip /SetProperty Manufacturer="Contoso Ltd"`, `/SetVersion 1.2.3`, `/AddFile APPDIR C:\bin\app.exe`.
  Several commands can run from a `;aic` command file with `/execute` (it needs an explicit `Save` line). There are
  no CLI commands for dialogs or controls.
- Anything else: replace the exact text in PowerShell. The file is UTF-8 without BOM; this keeps CRLF and encoding:

```powershell
$path = 'C:\src\Setup.aip'
$text = [IO.File]::ReadAllText($path)
$new = $text.Replace('Value="Your Company"', 'Value="Contoso"')   # include enough context to be unique
if ($new -eq $text) { throw 'Text to replace not found' }
[IO.File]::WriteAllText($path, $new, [Text.UTF8Encoding]::new($false))
```
- `/AddFile` chooses the feature for you. With several features, name the feature (and `-64bit_component` if
  needed) and verify where the component landed.
- `/build` does not modify the `.aip`.

## Verifying the package

Exit code 0 only means the build ran. Query the built MSI for what you changed:

```powershell
# Separate process: a COM handle keeps the MSI open, and a later build or msiexec then fails ("in use", 1619).
powershell -NoProfile -Command {
  $i = New-Object -ComObject WindowsInstaller.Installer
  $db = $i.OpenDatabase('C:\out\Setup.msi', 0)
  $v = $db.OpenView("SELECT ``Component_``, ``Feature_`` FROM ``FeatureComponents``"); $v.Execute()
  while ($r = $v.Fetch()) { '{0} -> {1}' -f $r.StringData(1), $r.StringData(2) }
}
```

- MSI SQL returns rows in no useful order, and `ORDER BY` on strings is not alphabetical. Sort in the script.
- Inside a PowerShell function, `$view.Execute()` and `$view.Close()` return `$null`, which becomes part of the
  function's output (empty items in the list). Write `[void]$view.Execute()`.
- Project build scripts often report problems with PowerShell `Write-Warning`. Redirected output drops those records
  unless captured (`3>&1`, `-WarningVariable`); capture them, they can be the only sign of a wrong package.
