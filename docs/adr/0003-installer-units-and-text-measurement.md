# ADR 0003 — Installer units, font selection and text measurement

- Date: 2026-10-11
- Status: Accepted

## Context

`lint_dialog` and `render_dialog` both need to turn the rows of an MSI's `Dialog` and `Control` tables into pixels
and decide whether a control's text fits. Three questions have to be answered once, for both tools:

1. How do the coordinates in the MSI tables map to screen pixels?
2. Which font is a control's text drawn with?
3. How do we measure text so that the result matches what Windows Installer shows?

What the documentation says:

- Coordinates and sizes in the `Dialog` and `Control` tables are **installer units**, "approximately equal to
  one-twelfth (1/12) the height of the 10-point MS Sans Serif font". In practice this is 4/3 px per unit at 96 DPI
  on both axes: the standard 370×270-unit dialog is drawn 493 px wide and its 370×234-unit background bitmap area is
  493×312 px; at 120 DPI the same dialog is 616 px wide, so the factor scales with DPI.
- The font comes from the package: a `{\style}` or `{&style}` prefix in the control's `Text`, otherwise the
  `DefaultUIFont` property, both naming a row of the `TextStyle` table (`FaceName`, `Size`, `Color`, `StyleBits`).
  Without `DefaultUIFont` the installer uses "the System font", and Microsoft says every package with a UI should
  set it.
- A Text control "can be created from the STATIC class" with `SS_LEFT`: GDI text that wraps at word boundaries and
  never grows. Its `Attributes` column changes that per control: `NoWrap` (`0x00040000`) shows a single line and
  cuts it with "...", `NoPrefix` (`0x00020000`) shows `&` as itself instead of underlining the next character,
  `Sunken` (`0x00000004`) adds a 3-D border.
- `Text` is a formatted string: `[ProductName]` and other properties are replaced at run time, so the string in the
  table is not always the string on screen.

Measurement on this machine (plain `DrawText` with word break on a memory DC; font em height
`round(points × dpi / 72)`; the sentence is a made-up 79-char hint, the label is 24 chars):

| DPI | Font | Line | 79-char hint in a 230-unit wide control | 18 / 20 units high | 24-char label vs. 125 units |
|---|---|---|---|---|---|
| 96 | Tahoma 8 pt | 13 px | 2 lines, 260 of 307 px wide, 26 px high | 24 / 27 px | 110 of 167 px |
| 96 | Segoe UI 9 pt | 15 px | 2 lines, 275 of 307 px wide, 30 px high | 24 / 27 px | 115 of 167 px |
| 120 | Tahoma 8 pt | 16 px | 2 lines, **381 of 383 px** wide, 32 px high | 30 / 33 px | 130 of 208 px |
| 144 | Tahoma 8 pt | 19 px | 2 lines, 457 of 460 px wide, 38 px high | 36 / 40 px | 157 of 250 px |

What this shows:

- With a 13 px line (Tahoma 8 or MS Sans Serif 8), two lines need 26 px: an 18-unit control (24 px) shows only the
  first line, ending in "...", a 20-unit control (27 px) shows both (confirmed in msiexec, see
  `docs/research/2026-10-11-control-calibration.md`). The baseline UI runs fixed a clipped label by exactly that change
  (height 18 → 20). With Segoe UI 9 (15 px line) both heights would clip, so that fix working is consistent with a
  13 px font in that project. To be confirmed by reading `DefaultUIFont` and `TextStyle` from a real MSI once the
  reader exists.
- Text width does not scale linearly with DPI: the same hint uses 85 % of the line at 96 DPI and 99 % at 120 DPI
  (2 px to spare), and the 20-unit control has 1 px of height to spare at 120 DPI. Line height does not either:
  Segoe UI 9 pt is 15 px a line at 96 DPI but 20 px at 120 DPI. A check at 100 % alone would miss controls that are
  tight at 125 %.

## Decision

### Units

- `px = round(units × 4/3 × dpi / 96)` on both axes, in one place (`InstallerUnits.ToPixels`).
- Every text check runs at **96, 120 and 144 DPI** (100 %, 125 %, 150 %). The result lists the verdict per DPI, so a
  developer on a 100 % screen can see that a finding only happens at 125 %.

### Font

- Resolved from the MSI: the `{\style}` / `{&style}` prefix of the control's `Text`, otherwise `DefaultUIFont`,
  looked up in `TextStyle`; `StyleBits` gives bold, italic, underline and strike-out.
- A missing `DefaultUIFont` is a **finding of its own**, since Microsoft says every UI package should set it. In that
  case measurement falls back to Segoe UI 9 pt and the verdict is marked approximate, naming the font actually used.
  This fallback is our choice, not a claim about what msiexec does.
- A face that is not installed on the machine running the server is **not** replaced by that fallback. `CreateFontW`
  never fails for an unknown face; GDI substitutes one (font mapper, `FontSubstitutes`), and msiexec on the same
  machine goes through the same substitution. So we keep GDI's
  choice, read its name back with `GetTextFaceW`, and mark the verdict approximate with that name. *(Amended
  2026-10-11 while implementing: the first version used Segoe UI 9 here too, which predicts the real dialog worse.)*
- A bitmap (raster) face such as `MS Sans Serif` comes in a few sizes made for 96 DPI. A verdict that fails only at
  120 or 144 DPI with such a face is marked approximate.

### Measurement

- **GDI `DrawTextW`** through our own P/Invoke (`CreateFontW`, `CreateCompatibleDC`, `SelectObject`, `DrawTextW`,
  `DeleteObject`, `DeleteDC`), the same way ADR 0001 wraps `msi.dll`. Font height is `-MulDiv(points, dpi, 72)`, so
  one memory DC measures any DPI without touching the machine's display settings.
- Flags follow the control's attributes: `DT_CALCRECT`, plus `DT_WORDBREAK` unless `NoWrap`, `DT_NOPREFIX` only with
  `NoPrefix`, `DT_RIGHT` with `RightAligned`. `Sunken` reduces the usable area by the border width.
- Per control type, v0.1:
  - `Text`: wrapping → fits if the wrapped height ≤ the control height and no word is wider than the control;
    `NoWrap` → fits if the width ≤ the control width (otherwise the user sees "...").
  - `CheckBox`: the label wraps inside the width left of the box (control width − 16 px at 96 DPI) and fits if the
    wrapped height ≤ the control height. *(Amended 2026-10-11: the first version said "one line"; msiexec wraps
    check box labels, which matters for tall check boxes with two-line labels.)*
  - `PushButton`: one line, centred; text width + 6 px ≤ control width.
  - The 16 px and 6 px come from a calibration against msiexec at 96 DPI
    (`docs/research/2026-10-11-control-calibration.md`, `tools/calibration/calibrate-controls.ps1`) and scale
    linearly with DPI. `RadioButton` items live in the `RadioButton` table and are not measured in v0.1, nor are
    other control types.
- Not measured, because their text is not what the user sees: controls whose `Text` an `EventMapping` row sets at
  run time (feature descriptions, progress text); zero-sized controls; controls without `Visible` that no
  `ControlCondition` row shows; check boxes no wider than their box (the label is a separate Text control);
  `Indirect` controls; buttons and check boxes with `Bitmap`, `Icon`, `PushLike` or `ElevationShield`.
- `[Property]` references are replaced by their value from the `Property` table. A reference without a value there
  (set at run time, `[#file]`, `[%ENV]`, or a value that is itself formatted) counts as empty, and the verdict says it
  is a **lower bound**. *(Amended 2026-10-11: the first version measured references as written, which is not a lower
  bound: `[ButtonText_Cancel]` is longer than "Cancel", and lint reported every standard Cancel button of real
  packages as clipped.)*
- `render_dialog` draws text through the same GDI path, so the picture and the lint verdict cannot disagree.

## Alternatives considered

- **`TextRenderer.MeasureText` (WinForms):** the same `DrawText` underneath, but it pulls in WinForms
  (`UseWindowsForms`) for one call, and the overload without an `IDeviceContext` ignores `NoPadding`: it adds the
  glyph-overhang padding (7 px for Tahoma 8 at 96 DPI) to every width. The first version of the table above was
  measured that way and its widths were 7 px too wide; corrected 2026-10-11, heights were unaffected.
- **`Graphics.MeasureString` (GDI+):** a different layout engine that adds its own padding; it does not predict a
  STATIC control.
- **Characters × average width:** what an agent does without the server. Fine as a hint, not as a verdict near the
  edge, which is exactly where clipping happens.
- **`MapDialogRect` / dialog base units:** the Win32 dialog template system, not Windows Installer's. Its factor
  depends on the dialog font; installer units do not.

## Consequences

- First `lint_dialog` tests (generated fixture packages, see ADR 0001): a 230×18 Text control with a two-line
  Tahoma 8 pt hint must be reported clipped, its 230×20 twin must pass at all three DPIs, the 24-char checkbox label
  in 125×12 must pass, a `NoWrap` control with an over-long label must be reported, and a text with a `[Property]`
  reference that has no value must come back marked as a lower bound.
- Results depend on the fonts installed on the machine running the server: normally the same machine the installer
  is tested on. The result always names the font it used.
- Check box and button margins are calibrated, not documented. If a Windows version changes them, only those
  constants move.

## Sources

- [Installer Units](https://learn.microsoft.com/en-us/windows/win32/msi/installer-units)
- [DefaultUIFont property](https://learn.microsoft.com/en-us/windows/win32/msi/defaultuifont)
- [Text Control](https://learn.microsoft.com/en-us/windows/win32/msi/text-control)
- [Advanced Installer forum: dialog image sizes for 370×270 units](https://www.advancedinstaller.com/forums/viewtopic.php?t=7930)
