# ADR 0006 — render_dialog: drawing engine, run-time state and output

- Date: 2026-10-12
- Status: Accepted

## Context

ADR 0001 puts `render_dialog` in v0.1: "draws an approximate PNG of the dialog from the same rows, without
installing anything". The baseline's most expensive task was the UI task (20–30 min, 12–22M cache-read tokens), and
most of that went into *seeing* the dialog: build, start the real installer, drive it through Win32 messages,
capture it with `PrintWindow`, which sometimes dropped labels. `lint_dialog` now answers "does the text fit, do
controls overlap, is the tab order closed". What it cannot answer is the question a person asks first: what does the
page look like, is it balanced, is that the right page.

What a spike showed (Advanced Installer 24.0's own MSI, the package from the README example; images in
`docs/images/render-spike-*.png`):

- GDI `DrawTextW` with the package's fonts plus uxtheme's `DrawThemeBackground` (class `Button`) for push buttons,
  radio buttons and check boxes, drawn into a memory bitmap with no window, gives a picture close to the real
  msiexec screen: same fonts, same line breaks, same "Do you also want to re…" and "Setup was I…" cut-offs as the
  screenshots `lint_dialog` was checked against, Windows 11 button and radio shapes.
- `OpenThemeDataForDpi(NULL, "Button", dpi)` works without a window and scales the theme parts: the 150 % render has
  correctly sized buttons and radios.
- Pictures: over the packages in `C:\Windows\Installer` on the development machine, `Bitmap` controls point at BMP
  (1,651), JPEG (79), PNG (21) and GIF (1) data in the `Binary` table, `Icon` controls at ICO (428). All five decode
  with GDI+.
- **Two controls with opposite `ControlCondition` rows were both drawn** (`Question` and `QuestionReplace` on
  `UpgradeDlg`, both authored `Visible`), one on top of the other. A picture has to pick one; `lint_dialog` does not.
- Access keys: `&Finish` was drawn with F underlined; msiexec hides the underline until Alt is pressed.
- GDI does not write an alpha channel. The spike drew on a surface without one (`Format32bppRgb`), filled the
  background first, and its text came through the PNG round trip intact.

From the documentation (Microsoft Learn, Control Attributes, Conditional Statement Syntax): Text controls have
`Transparent` (0x10000), `NoPrefix`, `NoWrap`, `Password` (0x200000); PushButton/CheckBox/RadioButton have `Bitmap`,
`Icon`, `FixedSize` (0x100000), `IconSize16/32/48`, `PushLike`, `ElevationShield` (PushButton, 0x800000);
`HasBorder` (RadioButtonGroup, 0x1000000). Conditions are a small expression language over properties.

## Decision

### Drawing engine

Own drawing into a memory bitmap: **text with the same `DrawTextW` call, font creation and DC settings that
`TextMeasurer` uses, so a label the picture shows cut off is exactly the label `lint_dialog` reports**, and theme
parts through uxtheme for the controls Windows draws with themes. Text flags: `DT_WORDBREAK | DT_EDITCONTROL |
DT_END_ELLIPSIS | DT_HIDEPREFIX` (single line for `NoWrap`, `DT_NOPREFIX` for `NoPrefix`, `DT_RIGHT` for
`RightAligned`), matching the calibrated msiexec behaviour (whole lines, the last one ending in "…").

`System.Drawing.Common` provides the surface (`Bitmap`, `Format32bppRgb`), the HDC (`Graphics.GetHdc`), picture
decoding (`Image.FromStream`) and PNG encoding. Rule: text is never drawn with `TextRenderer` or
`Graphics.DrawString`; both lay text out differently from a STATIC control (ADR 0003 paid 7 px for that lesson once).

Client area only, in the same pixel space as `lint_dialog`; no title bar or window frame. `scaling` 100, 125 or 150 %
(96/120/144 DPI), default 100.

### What each control type looks like

| Fidelity | Types | How |
|---|---|---|
| Faithful | Text, PushButton, CheckBox, RadioButtonGroup (its RadioButton rows), GroupBox, Line, Bitmap, Icon | Text as above; buttons, boxes and radios from the theme (default button state for `Control_Default`, checked state from the property value); pictures decoded from `Binary`, stretched unless `FixedSize`; `ElevationShield` from the system shield icon; disabled controls in the disabled state and gray text |
| Frame and value | Edit, PathEdit, MaskedEdit, ComboBox | Themed edit/combo frame with the property's value (dots for `Password`) |
| Labelled placeholder | ListBox, ListView, SelectionTree, DirectoryList, DirectoryCombo, VolumeCostList, VolumeSelectCombo, ProgressBar, ScrollableText, Billboard, Hyperlink, anything unknown | Sunken frame with the type name inside, so the layout is right and nobody mistakes it for content |

Pictures are drawn first, text and controls on top, so labels over banners stay readable as they are on screen.
A `Transparent` Text control does not erase its background; an opaque one fills its rectangle with the dialog
colour first.

### Run-time state: which controls are drawn

A control is drawn when it is visible **after the dialog's ControlCondition rows are evaluated** against the
`Property` table, with the caller's `properties` (name → value) applied on top. The evaluator implements the
documented syntax for properties: `"strings"`, integers, all logical, comparison, substring and bitwise operators
with their precedence, `~` for case-insensitive comparison. A condition that needs install-time state
(environment variables, feature and component states) or does not parse leaves the control as authored, and the
result lists it under `unevaluated`, so
the caller can pass the property values it needs and draw again. Properties a custom action would set are unknown
here, which is exactly why `properties` exists.

`[Property]` references in text resolve the same way. Unresolved references are drawn literally as `[NAME]` and
listed; `lint_dialog` measures them as empty (LowerBound), but a picture should not hide that text is missing.

### Output

A `CallToolResult` with two content blocks:

1. the PNG as an image block (`ImageContentBlock.FromBytes(png, "image/png")`), so the agent sees the dialog in the
   same turn;
2. a JSON text block: dialog, title, size in pixels, DPI, fonts used (and whether one was substituted), controls
   drawn as placeholders, conditional controls with the outcome or `unevaluated`, unresolved properties.

Optional `outputPath` also writes the PNG to disk (for a person, a pull request, a comparison). Annotations:
`ReadOnly = false` (it writes only when asked), `Destructive = false`, `Idempotent = true`, `OpenWorld = false`.

### Not in this version

Drawing `lint_dialog` findings on the picture (red boxes). The picture already shows cut-off text by construction;
a second visual language would need its own calibration. Can follow if the measurement shows agents miss findings.

## Alternatives considered

- **Real controls in a hidden window, captured with `WM_PRINT` / `PrintWindow`.** Closest in theory, but the
  baseline and the calibration both saw `PrintWindow` return controls unpainted, and it needs a message loop in the
  server process.
- **Run msiexec on a stripped copy and capture the screen** (how the README screenshots were made). The real thing,
  but it needs a modified copy of the package, `MsiEmbeddedUI` removed, the window on top of everything, and seconds
  per dialog; it changes nothing on disk only if every step is right.
- **WIC through COM instead of `System.Drawing.Common`.** No package, but several COM interfaces of P/Invoke for
  what four `System.Drawing` calls do; the package is Microsoft's and Windows-only like the server.
- **Draw every control as authored and ignore ControlCondition.** Simple, and wrong for the first real dialog
  tried.

## Consequences

- New package reference: `System.Drawing.Common`. ADR 0001's side effect "temporary PNG" becomes "PNG returned in
  the result; written to disk only with `outputPath`" (amended).
- The condition evaluator is shared code: ADR 0005 lists "two controls with conditions that do not actually exclude
  each other are missed" as a gap that an evaluator could close for `lint_dialog` later.
- Tests do not compare against stored PNGs: theme, ClearType and the Windows version change pixels from machine to
  machine. They check structure: the image is the dialog's pixel size, ink appears inside each drawn Text control
  and nowhere a control hides, the clipped fixture label's ink ends where `lint_dialog` says the text ends, a decoded
  picture lands in its rectangle, a condition picks one of two stacked controls. The opt-in `ADVINST_MCP_REAL_MSI`
  test also renders every dialog of the real package. The look itself was compared by eye against the msiexec
  screenshots in `docs/images` (all 21 dialogs of Advanced Installer 24.0's setup render); an automatic comparison
  would need a screen capture of msiexec on the test machine and is left out.
- Found while comparing: a TextStyle colour applies to Text controls only. Advanced Installer's buttons name a white
  style (`ImageButtonLight`) and still show black labels; a RadioButtonGroup paints its whole rectangle, which shows
  as a panel on a background picture; a picture button's Text can carry a font prefix before the picture's name.
- Whether the client shows the image is up to the client; the JSON block carries everything a client without image
  support needs to know what was drawn.

## Open points

- The panel behind the radio buttons and the button's shield icon in the msiexec screenshot did not appear in the
  spike; both come from controls or attributes the spike skipped and are covered by the table above.
- Themes other than the default Windows 11 light theme (high contrast, dark title bars) are not checked; msiexec
  dialogs do not follow dark mode.
