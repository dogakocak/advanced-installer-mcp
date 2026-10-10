# Baseline, arm A (2026-10-10)

Arm A = Claude Code with shell tools only, no MCP server, no skill file. Method: `measurement-method.md`.

## Subject project

A real-world Windows product setup (details withheld): one `.aip`, two builds (two brands), two Windows services
that depend on MSMQ, eleven PowerShell custom actions, custom dialogs for a SQL Server connection (with a Test
Connection button) and a message-queue address, a project build script that stamps the version and verifies the
built MSI tables, and a README that documents the project's Advanced Installer conventions. Advanced Installer 24.0.
Test database: a SQL Server with a self-signed certificate.

## Tasks

| Task | Prompt in short | Hidden difficulty |
|---|---|---|
| T1 version | Build both brands as version x.y.z | none — control task, shows the noise of the setup |
| T2 new file | A new DLL appeared in the second service's publish folder; package it | the documented `/AddFile` recipe puts the component into the wrong feature; component must be 64-bit |
| T3 service dependency + recovery | Second service depends on the first; restart on 1st/2nd failure after 60 s, nothing on the 3rd, reset after 1 day | Advanced Installer applies `AI_ServiceConfigFailureActions` in **days / minutes**, not the seconds / milliseconds the schema suggests |
| T4 broken build (v2) | Build; if something is wrong, find the root cause and fix it | build exits 0, but one component sits in the wrong feature; only the project script's warning reveals it; `AI_ICE*` "ERROR" lines are noise present on clean builds too |
| T5 end-to-end install | Install locally, verify services and configuration, uninstall | two real product bugs are reachable (see Findings) |
| T6 setup UI (v2) | Add a "Trust server certificate" checkbox bound to a property; new install checked, unchecked → `False`, Change shows the installed value; Next only after a successful connection test; after success either lock the fields or require a new test on change; Change with untouched fields needs no test; no text on the page may be clipped or overlap | an unchecked MSI CheckBox leaves its property **empty** (scripts treat empty as true); MSI Edit controls publish no change events; the maintenance UI runs unelevated and cannot read the locked config; one existing label on the page is already clipped (not named in the prompt) |

Discarded versions (kept, not compared): **T4 v1** — the break was an uncommitted change, every run found it with
`git diff` instead of from the build output; the prompt also said "make sure it is releasable", which triggered
install tests. **T6 v1** — only the checkbox; solved easily, so it measured the UI hypothesis weakly. A user review of
v1 found the two UI gaps that v2 adds.

## Results per task

| Task | Success | Interventions | Attempts (total) | Duration (min–max) | cache-read (min–max) | Assistant msgs | Advisor calls | Scope |
|---|---|---|---|---|---|---|---|---|
| T1 version | 3/3 | 0 | 0 | 1:00–1:40 | 205K–314K | 4–6 | 0/0/0 | — |
| T2 new file | 3/3 | 0 | 0 | 1:30–3:15 | 626K–842K | 9–11 | 0/1/0 | — |
| T3 service/recovery | 3/3 | 0 | 2 | 9:30–13:24 | 3.74M–6.63M | 35–58 | 2/2/2 | +, +, ++ |
| T4 v1 (discarded) | 3/3 | 0 | 0 | 9:55–13:42 | 2.21M–2.99M | 26–33 | 1/3/2 | ++, ++, ++ |
| **T4 broken build (v2)** | 3/3 | 0 | 1 | 1:41–2:04 | 455K–586K | 8–10 | 0/0/0 | — |
| T5 end-to-end install | 3/3 | 0 | 0 | 2:29–5:07 | 0.96M–1.37M | 14–17 | 0/0/1 | — |
| **T6 setup UI (v2)** | 3/3 | 0 | 2 | 20:41–29:31 | 12.24M–21.76M | 75–107 | 2/2/2 | — |

## Per run

Tokens = input / cache-write / cache-read / output, deduplicated by `message.id`.

| Run | First try | Attempts | Tokens | Duration | Notes |
|---|---|---|---|---|---|
| T1-1 | ✓ | 0 | 8 / 32.0K / 205K / 2.3K | 1:40 | filtered the build output itself; called the result "not releasable" (unsigned, binaries older than the version) |
| T1-2 | ✓ | 0 | 12 / 24.1K / 314K / 2.6K | 1:10 | read the full build output, then filtered warnings separately |
| T1-3 | ✓ | 0 | 8 / 28.3K / 212K / 2.4K | 1:00 | tried `Out-String \| Select -Last 1` to shorten output; one string, so it did not shorten |
| T2-1 | ✓ | 0 | 18 / 49.9K / 626K / 5.7K | 1:30 | `/AddFile` via CLI, then string replace for prefix, 64-bit and feature; kept CRLF on purpose; verified MSI tables through COM |
| T2-2 | ✓ | 0 | 24 / 50.9K / 842K / 5.2K | 3:15 | never used the CLI: copied neighbouring XML rows with the Edit tool, generated the GUID in PowerShell; its CRLF check was contradictory (Git Bash `grep -c $'\r$'` returned 0) but the file was fine |
| T2-3 | ✓ | 0 | 20 / 49.9K / 703K / 5.2K | 1:42 | README recipe verbatim: `/AddFile` + `/SetComponentAttribute -64bit_component`, then text fix for feature |
| T3-1 | ✓ (+) | 1 | 88 / 92.7K / 4.23M / 21.2K | 10:43 | wrote 86400 / 60000, installed + upgraded, read `FailureActions` from the registry, saw days/minutes, reverted to 1 / 1; extra related changes (service start order, build-script checks, README) |
| T3-2 | ✓ (+) | 1 | 74 / 97.0K / 3.74M / 24.3K | 9:30 | same story; **Git Bash `sed -i` broke the `.aip` CRLF**, noticed and repaired it with Python; also tested Change |
| T3-3 | ✓ (++) | 0 | 120 / 109.8K / 6.63M / 37.3K | 13:24 | wrote 1 / 1 from the start; tested install, Change, dependency stop/start, uninstall; **found the password-in-log bug** and fixed it with a build-script check, negative-tested |
| T4v1-1 | ✓ (++) | 0 | 54 / 80.7K / 2.21M / 23.5K | 9:55 | found the break via `git diff`; then install tests found the root cause of the uninstall leftover |
| T4v1-2 | ✓ (++) | 0 | 66 / 86.4K / 2.76M / 19.9K | 11:49 | same path; found the same leftover root cause independently |
| T4v1-3 | ✓ (++) | 0 | 70 / 77.1K / 2.99M / 20.3K | 13:42 | same path; third independent find, different spelling of the same condition |
| T4-1 | ✓ | 0 | 16 / 36.5K / 465K / 3.6K | 2:04 | signal from the build-script warning; grep in `.aip`; tied it to the README's `/AddFile` pitfall; no install test (narrowed prompt worked) |
| T4-2 | ✓ | 1 | 20 / 31.2K / 586K / 4.2K | 1:58 | same signal; **Git Bash `sed -i` broke CRLF again**, fixed with PowerShell |
| T4-3 | ✓ | 0 | 16 / 28.5K / 455K / 3.6K | 1:41 | same signal; after fixing, searched history with `git log --all -S` to attribute the cause |
| T5-1 | ✓ | 0 | 28 / 52.3K / 961K / 10.1K | 2:29 | install, services, config values, ACLs verified; reported both product bugs without fixing them (not asked); never printed the password |
| T5-2 | ✓ | 0 | 28 / 52.5K / 1.02M / 12.1K | 2:52 | caught the password leak because it contradicted the install guide's "password is never logged" |
| T5-3 | ✓ | 0 | 36 / 58.5K / 1.37M / 12.8K | 5:07 | confirmed the leak's root cause in the `CustomAction` table (no HideTarget) |
| T6v2-1 | ✓ | 1 | 156 / 208K / 13.97M / 72.2K | 26:00 | lock-after-success + "Edit Connection" button; found the clipped label itself (height 18→20); drove the real installer via Win32 and `PrintWindow` screenshots; installed, tested Change, uninstalled |
| T6v2-2 | ✓ | 1 | 158 / 353K / 12.24M / 60.1K | 20:41 | same design reached independently; label height 18→22 to match the other page; full-screen capture first grabbed whatever was in front, switched to `PrintWindow` |
| T6v2-3 | ✓ | 0 | 218 / 253K / 21.76M / 90.2K | 29:31 | lock-after-success, unlock only via Back (accepted, less discoverable); fixed a stale error text after a successful test; noticed `PrintWindow` dropped labels and re-captured from the real screen |

## Reading

- **Success and interventions are at the ceiling:** 18/18 compared runs passed at the first "done", none needed a
  human message. An MCP server cannot improve these; it can only regress them.
- **Where differences can show: cost and variance.** Runs of the same task differ 1.3–1.8× in tokens.
- **T3 cost is mostly discovery:** the days/minutes unit and self-started install tests. A skill file (arm B) should
  remove much of it; the server's own share (`B − C`) is measured separately.
- **T6 is the most expensive task by far** (2–4× T3, 50–100× T1). Every run rebuilt the same "see the dialog"
  machinery: build → start the real installer → UI Automation is useless (MSI controls are plain panes) → Win32
  messages (`WM_SETTEXT` does not commit the property until focus is lost) → `PrintWindow` (sometimes drops labels)
  → occasionally a real screen capture. The clipped label was found only through this loop. The value of
  `render_dialog` / `lint_dialog` is not "being able to" but doing it **without an install, cheaply and the same way
  every time**.
- **Path variance:** three T2 runs edited the `.aip` three different ways; the CLI alone never produced the right
  result. Git Bash `sed -i` broke CRLF twice despite a project rule against it. A deterministic edit tool removes
  this class of error.
- **Scope follows the prompt:** prompts that said "make it releasable / fix what you find" (T3, T4 v1) widened scope
  and surfaced real bugs; T5, which asked for no fixes, only reported.

## Findings in the subject project (generalised)

Both were fixed in the project afterwards; the lessons are in `advanced-installer-pitfalls.md`.

1. **Password in the verbose MSI log:** a deferred PowerShell custom action received the database password through
   `CustomActionData` without the HideTarget flag (0x2000). `MsiHiddenProperties` hides property lines, not the
   deferred action's data. Found by T3-3, confirmed by all T5 runs and two T6 runs.
2. **Configuration file restored after uninstall:** `AI_PersistentData` rows used `OLDPRODUCTS OR Installed`;
   `Installed` is also true on a real uninstall, so the backed-up config (with the password) was restored after the
   cleanup action had deleted it. Found independently three times (T4 v1), confirmed by T5 and T6.
3. **Not a bug:** recovery settings that looked wrong (`1` / `1`) were correct once the days/minutes unit was known.
