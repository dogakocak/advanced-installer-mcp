# Setup UI review: wireframes before dialogs

Before you add or change setup dialogs, show every screen of the wizard as a wireframe and wait for the user's
explicit approval. Do this even when the user did not ask for a preview. Do not touch the `.aip` until they approve.
Then build the dialogs to match the approved wireframes exactly.

## What to show

1. **All screens in wizard order**, the unchanged standard ones (Welcome, Ready, Progress, Finish) included, so the
   user sees the whole flow. Draw them with the classes in `wireframe-template.html` (next to this file):
   - if your client renders HTML inline in the chat, render it there;
   - otherwise copy the template into the project's scratch area, fill it in and open it.
2. **One or two lines per screen**: what it asks and how it behaves (when Next is disabled, which fields a choice
   enables).
3. **An input table** with one row per input:

   | Property | Control | Why this control | Default | Unattended |
   |---|---|---|---|---|

4. **The MSI limits that changed the design**, for example "no file picker in plain MSI" or "check is repeated in
   the execute sequence".

When a screen has a real design choice, show the variants side by side (A/B) and recommend one.

## Draw only what MSI can show

A wireframe is a promise. Draw only these controls: Text, Edit (plain, Password, Integer), MaskedEdit, PathEdit,
PushButton, CheckBox, RadioButtonGroup, ComboBox, ListBox, ListView, GroupBox, ScrollableText, ProgressBar, Bitmap,
Icon, Line, SelectionTree, VolumeCostList, DirectoryCombo/DirectoryList.

MSI has none of the following, so do not draw them:

- toggle switches, chips, cards, rounded or shadowed panels;
- placeholder text inside an edit (write a hint as a separate Text below it);
- validation while typing (Edit controls publish no change events; validate on Next or with a button);
- icons inside buttons or fields (only separate Bitmap/Icon controls);
- coloured or bold text on buttons, check boxes or radio buttons (TextStyle colour and weight apply to Text only);
- a file picker (plain MSI browses folders only; for a file, use Advanced Installer's browse-for-file custom action
  or a typed path);
- text placed inside a RadioButtonGroup's rectangle (the group paints its area and covers it).

Keep the standard layout: banner 370 × 44 units with title and description, body from y 52 to 226, a line at
y 234, and Back / Next / Cancel at y 243.

## Choose the control from the value

| The value is | Use | Not |
|---|---|---|
| yes / no | CheckBox | two radio buttons, a Yes/No combo |
| 2–4 fixed, mutually exclusive values | RadioButtonGroup (all options visible) | Edit, ComboBox |
| 5 or more fixed values | ComboBox, list-only | Edit, a long radio list |
| a folder | PathEdit + Browse | Edit |
| a file | Edit + Browse (custom action) or Edit | PathEdit (folders only) |
| a number | Edit with the Integer attribute; MaskedEdit for a fixed shape | plain Edit |
| a secret | Edit with the Password attribute | plain Edit |
| open text (host, user name) | Edit, with a hint Text below | ComboBox |

A choice that enables more fields (a "custom" option) is a RadioButtonGroup; the dependent fields sit indented
under it and are disabled by ControlCondition while another option is selected.

## Every input works unattended

Under `/qn` there is no UI. Every input on a dialog must be settable from the command line and checked without
the dialog:

- **Public property**: the name is all upper case (`SERVER_HOST`), or msiexec ignores it on the command line.
- **Secure**: add it to `SecureCustomProperties` when deferred actions or a managed install read it.
- **A default** that is valid when nobody types anything, or an explicit error when it is required and missing.
- **Check boxes map to explicit values**: an unchecked CheckBox leaves its property empty. Bind the box to a UI-only
  property (`START_SERVICE_UI`) and set the public one (`START_SERVICE`) to `1` or `0` in the Next button's events.
- **Validation runs in the execute sequence too**: a "Test connection" button or a Next condition does not run
  under `/qn`. Repeat the check as an immediate action in `InstallExecuteSequence` that fails the install.
- **Secrets** go into `MsiHiddenProperties` so they do not appear in a verbose log.

Show the resulting command line with the review:

```
msiexec /i Product.msi /qn /l*v install.log APPDIR="D:\App" SERVER_HOST=host.example SERVER_PROTOCOL=https START_SERVICE=1
```

## After approval

1. Build the dialogs with the controls, texts, properties and conditions of the approved wireframes, nothing more.
   A change you find necessary while building goes back to the user as an updated wireframe.
2. `build`, then `lint_dialog` on the package, then `render_dialog` for each changed dialog: compare the render
   with the approved wireframe, screen by screen.
3. `drive_dialog` for the behaviour the review promised (Next disabled until…, a choice enabling fields).
4. Check unattended: run the documented command line against the built MSI, or at least verify in the `Property`,
   `ControlEvent` and `InstallExecuteSequence` tables that every input is public, mapped and validated.
