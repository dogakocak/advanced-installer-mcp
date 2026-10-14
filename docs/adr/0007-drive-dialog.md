# ADR 0007 — drive_dialog: playing a dialog's events without installing

- Date: 2026-10-14
- Status: Accepted

## Context

The arm C measurement (`docs/research/2026-10-14-arms-b-and-c.md`) showed where v0.1 stops. On the setup UI task
`lint_dialog` and `render_dialog` replaced the agent's own measuring and screenshots, and two of three runs never
opened the real wizard. The third run (and one skill-only run) still wrote a Win32 driver, started msiexec, typed into
the page, pressed Test Connection and read the result, because the question it had to answer was behaviour, not
looks: *after I set these values and press this button, is Next enabled, which properties changed, which dialog
opens?* `render_dialog` shows one state; the caller has to guess the properties a button would set. That guess is
exactly where a UI change goes wrong (the unchecked check box that leaves its property empty, the flag a test sets
only on success).

A spike (2026-10-14, the subject project's built MSI, Windows Installer through its COM API) answered whether
Windows Installer itself can play a dialog without installing:

- `OpenPackage(path, msiOpenPackageFlagsIgnoreMachineState)` gives a session on the package with no install and no
  elevation. `EvaluateCondition`, `Property` get/set and `FormatRecord` work on it.
- `DoAction` in that session runs **built-in actions** (`FindRelatedProducts`, `CostInitialize`, `FileCost`,
  `CostFinalize`; `AppSearch` had no table in this package) and **custom actions that set properties** (type 51,
  also with the first-sequence flag, 307) for real. Type 35 (set directory) is the same kind of action by the
  documentation but did not occur in the package.
- It refuses every custom action that runs package code — DLL, EXE, script, and Advanced Installer's PowerShell
  launcher: the log says `Action '<name>' is not permitted in a restricted engine.` and `DoAction` returns
  "no action". A session opened from outside msiexec is a *restricted engine* by design.
- Walking `InstallUISequence` in order (skipping modeless dialogs, stopping at the first modal dialog) reproduces the
  property state the wizard opens with, as far as property-setting actions build it.
- `Installed` can be set on the session; with it, the maintenance-only property setters ran and the page's
  conditions came out as they do in Change.
- Playing the `ControlEvent` rows of a button in `Ordering` order — `[Property]` events through `FormatRecord`,
  `DoAction` through the session — gave the same sequence of property changes the real wizard makes, up to the
  refused test action. The whole spike ran in milliseconds.

## Decision

A new tool, **`drive_dialog`**, plays one dialog of a built MSI in a **restricted Windows Installer session** and
reports how the page changes, step by step. Nothing is installed, no package code runs, no elevation is needed.

### Session

Open the package with `MsiOpenPackageEx(path, MSIOPENPACKAGEFLAGS_IGNOREMACHINESTATE)`, UI level none. Apply
`maintenance: true` as `Installed = 1` (Change/Repair/Remove pages). Then run `InstallUISequence` from the start:
every positive-sequence row whose condition holds, through `MsiDoAction`, skipping dialogs, until the first modal
dialog whose condition holds. This is the state a person sees when the wizard's first page opens. The caller's
`properties` are applied **before** the walk (command-line properties) — that is how msiexec does it.

Windows Installer's own engine is the authority for conditions, formatting and built-in actions. The tool does not
use our `ConditionEvaluator` for anything the engine can answer: feature and component states (`&Feature`), `~=`,
version comparisons and directory properties come out exactly as msiexec computes them.

### Steps

The caller passes an ordered list of steps on the named dialog:

| Step | Meaning | How |
|---|---|---|
| `{"set": {"PROP": "value"}}` | the user typed into an edit, picked a radio button, checked a box | `MsiSetProperty`; an unchecked check box is `""`, as Windows Installer leaves it |
| `{"click": "Control"}` | the user pressed a push button (or a check box / radio with events) | play the control's `ControlEvent` rows in `Ordering` order, each only when its condition holds now |

Events while playing a click:

| Event | Played as |
|---|---|
| `[Property]` | `MsiFormatRecord` of the argument, then `MsiSetProperty` (empty value deletes the property) |
| `DoAction` | `MsiDoAction`. A property-setting action runs. An action the restricted engine refuses is **not run**: if the caller gave `actionResults[action]` (property → value), those values are set instead and the step says so; otherwise the step reports it as *not run, outcome unknown* and the caller can try again with `actionResults` |
| `NewDialog` | the page is replaced; following steps act on the new dialog |
| `SpawnDialog`, `SpawnWaitDialog` | reported as opened (e.g. the error box), with its text formatted; the current page stays, as when the user closes the box |
| `EndDialog` | reported (`Return`, `Exit`, `Retry`, `Ignore`); `Return` on the last page ends the wizard, later steps are an error |
| `Reset`, `AddLocal`, `Remove`, `SetTargetPath`, `CheckTargetPath`, others | reported as *not played* in this version |

After every step the ControlCondition rows of the current dialog are evaluated by the engine, as Windows Installer
does whenever a property changes.

### Output

A JSON report with one entry per step: the property changes (old → new), actions run, actions not run (and why),
dialogs opened or replaced, and the controls whose visible/enabled state changed. Then the final page: current
dialog, every control's visible/enabled state, and a PNG of it drawn by `render_dialog`'s painter **with the
engine's state**, not a second evaluation, so picture and report cannot disagree. `render: false` skips the picture.

Values of properties listed in `MsiHiddenProperties` (passwords) are shown as `***` in the report, also when the
caller set them; the agent already knows what it typed, and transcripts get pasted around.

Annotations: `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`.

## Alternatives considered

- **Drive the real wizard** (msiexec with UI, Win32 messages, screen capture) — what agents did by hand in the
  baseline and in two measured runs. It runs the package's real custom actions, which is its only advantage and
  also its cost: a test button connects to a real database, a "next" can start an install, it needs the desktop,
  sometimes elevation, and seconds to minutes per step. `show-dialog.ps1` in the skill stays for the real look.
- **Our own condition evaluator and event player, no Windows Installer session.** Works without msi.dll's engine,
  but loses built-in actions (`CostFinalize`, `FindRelatedProducts`), feature states and Windows Installer's exact
  formatting; every difference would be a false answer about the package's behaviour.
- **Run the package's script custom actions ourselves** (extract the PowerShell from the `Binary` table and run it).
  Gives the real test result, but executes package code on the caller's machine, with its side effects, and is
  Advanced Installer specific. Out of scope for v0.2; an opt-in can be argued in ADR 0004 (install security).
- **Extend `render_dialog` with steps.** One tool fewer, but mixes "show this state" (cheap, no engine) with "play
  these steps" (engine session, a report per step); a separate tool keeps both descriptions short and exact.

## Consequences

- **One session per process.** A second `MsiOpenPackage` while one is open in the same process fails with 1618
  ("another installation is already in progress"); separate processes can each hold one (checked 2026-10-14), so an
  install running elsewhere does not block the tool. The server serialises `drive_dialog` calls, and tests that
  open sessions share one non-parallel collection.
- New P/Invoke surface over msi.dll: `MsiOpenPackageEx`, `MsiDoAction`, `MsiSetProperty`, `MsiGetProperty`,
  `MsiEvaluateCondition`, `MsiFormatRecord`, `MsiSetInternalUI`. The session handle is one more `MsiHandle`.
- The tool tells *what the dialog does* only as far as property-setting logic goes. A page whose behaviour hangs on
  a code custom action (a connection test) is played with the caller's `actionResults` — the same "you supply what a
  custom action would set" contract `render_dialog` has, now per action and per step.
- `render_dialog`'s painter gets an entry point that takes a ready control state. `render_dialog` itself keeps its
  own evaluator for now (no session, works on any MSI database); whether it should switch to the engine is an open
  point.
- Tests use generated MSIs as before: a dialog with a check box, a test button whose `DoAction` points at a
  type-1 action (refused) and a type-51 action (run), conditions on Next, a `SpawnDialog` error box. They assert the
  step report and the final state. The opt-in real-package test plays the first page of every UI package found.

## Open points

- `Ordering` ties and rows without `Ordering`: Windows Installer documents no order for them; the spike's package
  had none. Played in table order and flagged in the report until a real package shows otherwise.
- An Edit control commits its property when it loses focus; `set` commits at once. Pages that react to the moment of
  commit (none known) would differ.
- Billboard, progress and other execute-time events are out of scope: the tool stops where the wizard would start
  installing.
