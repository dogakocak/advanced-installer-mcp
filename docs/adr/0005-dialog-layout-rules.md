# ADR 0005 — lint_dialog layout rules: bounds, overlaps, tab chain, embedded UI

- Date: 2026-10-11
- Status: Accepted

## Context

ADR 0001 gives `lint_dialog` four jobs: bounds, overlaps, the tab chain and whether text fits. ADR 0003 settled the
text check. This ADR settles the other three, plus one package-level warning that came out of the first real use.

What the documentation says (Microsoft Learn, Control, Dialog and MsiEmbeddedUI tables):

- `X`, `Y`, `Width`, `Height` are installer units and must be non-negative. The dialog's `Width` and `Height` are
  the size of its client area, so a control fits when `X + Width <= Dialog.Width` and `Y + Height <= Dialog.Height`.
- `Control_Next`: "The links between the controls must form a closed cycle. Some controls, such as static text
  controls, can be left out of the cycle." Tab moves the focus along these links.
- `Control_First`, `Control_Default`, `Control_Cancel` name controls of the same dialog. Microsoft recommends a Text
  control as `Control_First` when it labels the first input, for screen readers, so `Control_First` itself does not
  have to be on the cycle. All three columns are ignored in an error dialog (`Attributes` bit `0x00010000`).
- `MsiEmbeddedUI`: the row with `Attributes` bit `0x01` is the package's own UI DLL. When it runs, Windows Installer
  does not draw the `Dialog` table at all.

What the first real run showed (Advanced Installer 24.0's own setup, README example):

- Its dialogs put text on top of `Bitmap` controls (logo, banner) on purpose.
- `Question` and `QuestionReplace` on `UpgradeDlg` share one rectangle; `ControlCondition` hides one when
  `AI_REPLACE_PRODUCTS` is set and the other when it is not.
- It has an embedded UI, so its users normally never see the dialogs `lint_dialog` checks.

## Decision

A control takes part in these checks when it can be seen: width and height above zero, and either the `Visible`
attribute or a `ControlCondition` row that shows it. That is the same rule the text check already uses.

Bounds and overlaps compare **what a control paints**, not its rectangle, in pixels at 96, 120 and 144 DPI like the
text check. A first version compared rectangles in installer units and reported 3,248 overlap errors in all 95
packages with a UI on the development machine; nearly all were layouts that look right on screen. What a control
paints:

- A Text control: the area its text takes up, measured as in ADR 0003, from the top-left corner (top-right with
  `RightAligned`) and cut to the control; when the text is too high, the whole lines that fit. Standard templates
  make text boxes taller than their text, so the boxes reach into the next control while the text does not. A Text
  control without text paints nothing. A `Sunken` one paints its whole border. Text set by an `EventMapping` at run
  time is unknown and assumed to fill the control.
- A `ComboBox`, `DirectoryCombo` or `VolumeSelectCombo`: its closed box, one line of the `DefaultUIFont` plus 8 px
  (approximate, not calibrated). Their `Height` includes the dropped-down list, which covers other controls only
  while it is open.
- A `RadioButtonGroup`: its buttons from the `RadioButton` table, placed relative to the group. Standard maintenance
  dialogs put icons and descriptions between the buttons of one tall group.
- Everything else: its rectangle.

**Bounds** (all control types except `Bitmap` and `Line`, which are often drawn a few units wider than the dialog and
only lose an edge nobody misses):

- `control-out-of-bounds`, Error: what the control paints reaches past the dialog's client area at 96 DPI and is cut
  off; Warning when that only happens at 120 or 144 DPI.
- `control-off-dialog`, Warning: the whole control rectangle lies outside. It can never be seen; sometimes that is a
  deliberate way to hide a control, so it is not an error.

**Overlaps** (pairs of controls whose painted areas share pixels; touching edges are normal layout):

- Background and frame controls are left out: `Bitmap`, `GroupBox`, `Line`, `Billboard`. Text on a banner bitmap
  and controls inside a group box are the normal layout.
- When one of the two is text, an overlap of up to 2 px (at 96 DPI, scaled) is ignored: a line of text is as high as
  the font's cell, with empty room above and below the letters.
- Not reported: two buttons of an error dialog (it stacks OK, Cancel, Retry, ... in one place and Windows Installer
  shows the ones the message needs), and two Text controls with the same text (an embossed or shadowed label).
- `controls-overlap`, Error when the overlap shows at 96 DPI, neither control has a `Show` or `Hide` condition and
  no text involved is set at run time: both are on screen together.
- Warning when it only shows at 120 or 144 DPI, when exactly one of them has such a condition (they overlap whenever
  the condition shows it), or when a text set at run time was assumed to fill its control.
- Not reported when both have a condition: that is the usual way to swap two controls in one place, and telling
  whether two conditions exclude each other needs a condition evaluator, which v0.1 does not have.

**Tab chain** (skipped for error dialogs):

- `dialog-control-missing`, Error: `Control_First`, `Control_Default` or `Control_Cancel` names no control of the
  dialog.
- `tab-next-missing`, Error: a `Control_Next` names no control of the dialog.
- `tab-chain-open`, Error: a control is reached by a `Control_Next` link but has no `Control_Next` itself. Tab
  stops there instead of going round.
- `not-in-tab-order`, Warning: a control the user operates is not on a cycle and is not `Control_First`, so the
  keyboard cannot reach it. Those controls: `CheckBox`, `ComboBox`, `DirectoryCombo`, `DirectoryList`, `Edit`,
  `Hyperlink`, `ListView`, `MaskedEdit`, `PathEdit`, `PushButton`, `RadioButtonGroup`, `SelectionTree`,
  `VolumeSelectCombo`; only when they can be seen and are enabled (the `Enabled` attribute or an `Enable`
  condition). `ListBox`, `VolumeCostList` and `ScrollableText` can take the focus too, but the standard
  `FilesInUse`, `MsiRMFilesInUse` and disk space dialogs leave their information lists off the cycle on purpose (410
  warnings in 92 packages when they were included).
- `tab-chain-split`, Warning: the controls that take the focus are spread over more than one cycle, so Tab never
  moves from one group to the other.

**Embedded UI** (package level):

- `embedded-ui`, Warning, first in the report: the package has an `MsiEmbeddedUI` row with bit `0x01`. Its users
  normally see that UI; the findings apply to the Windows Installer dialogs it falls back to.

Severity keeps its meaning from ADR 0003 for text, extended: Error is a problem every user who reaches the dialog
sees at 100 % scaling; Warning is one that depends on scaling, on a condition, or on something the package does at
run time.

## Consequences

- Over the 95 packages with a UI on the development machine (2026-10-11) the rules report 16 overlap errors in 8
  packages (two layouts of one vendor template: buttons sharing a unit, a list running under radio buttons), 12
  overlap warnings, 13 `not-in-tab-order` warnings in 5 packages, no bounds findings and the `embedded-ui` warning
  once. Before the painted-area model: 3,248 overlap errors and 81 bounds errors.
- Bounds and overlaps need the package's fonts, like the text check; the tab chain does not.
- Known false positive: a small button drawn on the edge of a bigger one on purpose, like the drop-down arrow of a
  split button (`CustomInstallBtn` on `Next` in Advanced Installer 24.0's `WelcomeDlg`). It is a Warning there
  because a condition shows it; an exemption for a button entirely inside another button can follow if it shows up
  unconditioned.
- Two controls with conditions that do not actually exclude each other are missed. A condition evaluator could
  close that gap later.
- Visual tab order (top to bottom, left to right) is not checked; it is a matter of taste more often than a bug.
- The background/frame list and the focusable list are decisions, not facts from the documentation; real packages
  that disagree with them change this ADR.
