# ADR 0009 — lint rules for setup inputs that must also work unattended

- Date: 2026-11-05
- Status: Proposed

## Context

Every value a setup page asks for must also be settable without the page: `msiexec /i Setup.msi /qn PROP=value`.
And a value typed on a page must reach the execute sequence, where the install actually uses it. Both fail
silently, the build succeeds and the wizard looks right:

- A **private property** (any lowercase letter in the name) cannot be set on the command line and is not passed from
  the UI (client) to the execute sequence (server).
- A **public property not listed in `SecureCustomProperties`** is dropped on the way to the server when the install
  runs elevated, the normal case for per-machine packages: the page shows the value, the install uses the default.
- A **password Edit whose property is not in `MsiHiddenProperties`** is written to verbose logs in clear text.
- A **RadioButtonGroup without a default** opens with no button selected, and `/qn` without the property passes an
  empty value that is none of the choices.

A probe (2026-11-05) over four packages built by Advanced Installer, with these candidate rules:

- **Advanced Installer does not fill `SecureCustomProperties` itself.** The built package's list is exactly the
  `.aip`'s row (23 = 23 in one package); properties the user adds to pages are secure only when someone adds them.
- One package had three page inputs (database server, user, password) not in `SecureCustomProperties` and used by a
  property-setting action in `InstallExecuteSequence`, and two password fields missing from `MsiHiddenProperties`.
  The other three had none.
- A first version counted every `CustomAction` row as "execute side" and flagged a property used only by a type 51
  action in `InstallUISequence`. Counting only actions scheduled in an execute sequence removed it; after that the
  probe had no false positive.

## Decision

New rules in `lint_dialog` (`Lint/UnattendedRules.cs`), applied to the **input controls** of the checked dialogs:
`Edit`, `MaskedEdit`, `PathEdit`, `CheckBox`, `RadioButtonGroup`, `ComboBox`, `ListBox`. Findings name
`Dialog.Control` and the property, like the existing rules.

### "Used by the install"

A property is used by the install when its name appears (as `[PROP]` or as a condition term) in

- a row of `CustomAction` whose action is scheduled in `InstallExecuteSequence`, `AdminExecuteSequence` or
  `AdvtExecuteSequence`;
- a condition of a row in those execute sequences, or a `LaunchCondition`;
- any other table that is not UI-only. UI-only: `Dialog`, `Control`, `ControlEvent`, `ControlCondition`,
  `EventMapping`, `RadioButton`, `ComboBox`, `ListBox`, `ListView`, `CheckBox`, `TextStyle`, `UIText`, `Binary`,
  `Icon`, `Property`, `InstallUISequence`, `AdminUISequence`.

Advanced Installer's own tables (service settings, file updates, ...) count automatically: they are not UI tables.

### Rules

| Rule | Severity | When |
|---|---|---|
| `input-property-private` | Error | the input's property has a lowercase letter and is used by the install |
| `input-property-not-secure` | Error | the property is public, used by the install, not in `SecureCustomProperties`, and not a `Directory` key |
| `password-not-hidden` | Warning | an `Edit` with the Password attribute (0x200000) whose property is not in `MsiHiddenProperties` |
| `choice-without-default` | Warning (RadioButtonGroup), Info (ComboBox) | the property has no `Property` row |
| `control-choice` | Info | a RadioButtonGroup with more than 5 buttons (a drop-down list reads better), or a drop-down `ComboBox` (ComboList attribute) with 3 items or fewer (radio buttons show all choices at once) |

Each finding says what to change: add the property to `SecureCustomProperties` / `MsiHiddenProperties`, rename it
uppercase, add a `Property` row, use the other control.

`Directory` keys (`APPDIR`, `TARGETDIR`) are left out of `input-property-not-secure`: a target path chosen on a page
travels through directory resolution (`SetTargetPath`, `CostFinalize`), not as a plain property, and none of the
probe's packages secures them. This is checked against a real elevated install before the rule ships (open point).

Properties a page only uses for itself (an `IAgree` check box, a UI-only flag mapped to another property) are not
used by the install and produce no finding.

### mock_dialog

A mock has no execute sequence, so the first two rules cannot fire on it; `mock_dialog` refuses lowercase input
properties up front (ADR 0008) and reports `password-not-hidden`, `choice-without-default` and `control-choice`.

## Alternatives considered

- **A rule for "validation only in the UI"** (a value checked by a button's custom action and nowhere in the execute
  sequence, so `/qn` skips the check). Not decidable from the tables: what a DLL or script action validates is in its
  code. A rule would guess and be noisy. The skill asks for it on the approval page instead ("where is this input
  checked under `/qn`"), and `drive_dialog` shows what the button does.
- **Flag every public input missing from `SecureCustomProperties`.** Simple, but most page-only properties
  (`RUNAPPLICATION`, `VIEWREADME`, UI flags) would be findings; the probe's real defects would drown.
- **A text search for the name anywhere in the package.** Catches UI-sequence actions too; that was the probe's one
  false positive.

## Consequences

- `lint_dialog` reads more tables (every non-UI table's string columns, the execute sequences), once per package,
  not per dialog.
- **Indirect use is not seen**: a page that builds a connection string from several inputs in a UI action and passes
  only the result hides the parts. Those parts produce no finding (a missed defect, never a false alarm).
- Tests: generated MSI pairs (broken / fixed) per rule, including the UI-only type 51 action that must not count.
  The opt-in real-package test runs the rules over the packages it finds.

## Open points

- **Directory properties under elevation**: confirm with an elevated per-machine install that a page's `APPDIR`
  choice reaches the execute sequence without `SecureCustomProperties`. If it does not, `Directory` keys join the
  rule.
- Command-line properties under `/qn` for a non-administrator with elevated-install policy are restricted the same
  way; the rules do not model install policies.
