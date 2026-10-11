# Dialogs

The `.aip` holds dialogs as Windows Installer `Dialog`, `Control`, `ControlEvent`, `ControlCondition`, `TextStyle` and
`RadioButton` rows; the built MSI has the same tables. Read and check them there.

## Sizes and fonts

- Positions and sizes are installer units: **1 unit = 4/3 px at 100 % scaling** (96 DPI), scaled with DPI.
  A 370 × 270 dialog is 493 × 360 px.
- Text uses the `TextStyle` named by a `{\Style}` or `{&Style}` prefix (or a property holding one), else the style
  named by the `DefaultUIFont` property. Size is in points: px = pt × DPI / 72.
- A `TextStyle` colour applies to Text controls only. Buttons, check boxes and radio buttons draw labels in the system
  colour.

## Text that fits

Measure text with GDI `DrawText` (`DT_CALCRECT`) in the dialog's font, at 100, 125 and 150 %. Character counts are
not good enough. `TextRenderer.MeasureText` without a device context adds padding and overstates widths by ~7 px.

| Control | Fits when (96 DPI) | Too small looks like |
|---|---|---|
| Text | wrapped height ≤ control height | the lines that fit, the last ending in "…"; text never grows |
| CheckBox | text width + 16 px ≤ control width | label wraps and is cut at top and bottom, no "…" |
| PushButton | text width + 6 px ≤ control width, one line | label cut |

A label with `[Property]` references is only a lower bound when the property is set at run time.

## Behaviour

- **An unchecked CheckBox leaves its property empty**, not `False`. If later logic treats "anything but False" as
  true, bind the box to a UI-only property and map it to `True`/`False` in the Next button's events.
- **Edit controls publish no change events.** A "test passed" flag cannot be reset when the user edits a field: lock
  the fields after success (`ControlCondition` Disable on the flag, with an explicit unlock) or compare current and
  tested values in the Next condition.
- `ControlCondition` rows (Show, Hide, Enable, Disable, Default) are evaluated when the dialog opens and when a
  property changes. Two controls in one place, shown by opposite conditions, are a normal pattern.
- **The maintenance (Change) UI runs unelevated**: it cannot read files ACL'd to SYSTEM/Administrators.
- **There is no UI under `/qn`.** Whatever only a dialog button validates is not validated in a silent install
  unless an execute-sequence action repeats it.
- A result shown in an MSI dialog looks like Windows Installer's own error dialogs, not like a message box; ask
  which the team expects.
- If the MSI has an `MsiEmbeddedUI` row with attribute 1, users see that embedded UI, and the `Dialog` table is only
  the fallback.

## Seeing a dialog without installing

Run `show-dialog.ps1` (next to this file):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File show-dialog.ps1 -Msi .\Setup.msi -Dialog WelcomeDlg -Png .\welcome.png -Properties 'NAME=value'
```

It works on a copy: deletes both sequences, the custom actions and the embedded UI, puts only the dialog in the UI
sequence, makes its buttons end the dialog, gives the copy a new ProductCode **and a new package code**, opens it
with msiexec (nothing can be installed), captures the window and closes it. Why each step matters:

- Same package code as an installed product: msiexec runs the installed product's cached package instead of the copy.
- `PrintWindow` returns some static and button controls unpainted; capture from the screen.
- A background process cannot bring a window to the front (`SetForegroundWindow` is refused); a screen capture then
  shows whatever covers the dialog. The script makes the window topmost and refuses to save when it is covered.

Driving a real installer instead: UI Automation shows MSI controls as plain panes; `WM_SETTEXT` changes an edit box
but the property is committed only when the control loses focus; each wizard page is a new window.
