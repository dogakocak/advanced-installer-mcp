# Advanced Installer pitfalls seen in the baseline

Facts an agent had to (re)discover during the baseline runs. Each one cost time or caused a bug. They are input for
the skill file (arm B) and for tool design (arm C).

## Project file and build

- **`.aip` files are CRLF.** Git Bash `sed -i` rewrites them with LF and Advanced Installer then misbehaves. Edit
  through the Advanced Installer CLI, PowerShell, Python with explicit newline handling, or a dedicated tool. Check
  with `file` (CRLF line terminators) — `grep -c $'\r$'` from Git Bash can report 0 on a correct file.
- **`/AddFile` picks the feature for you.** On a project with several features the new component can land in the
  wrong one; set the feature (and `-64bit_component`) explicitly and verify in the built MSI.
- **Build exit code 0 does not mean the package is right.** Missing or misplaced content can still build. Verify the
  MSI tables (`File`, `Component`, `FeatureComponents`, …) after every build.
- **`ERROR: AI_ICE…` lines appear on clean builds.** They are validation notes, not build errors. A log parser must
  not count them as errors.
- **PowerShell drops `Write-Warning` records when output is redirected** unless they are captured
  (`-WarningVariable`, `3>&1`). Agents repeatedly lost the one warning that mattered.
- **A COM handle to an MSI keeps it locked** for the rest of the process; a rebuild in the same PowerShell process
  then fails with "file in use". Release handles or query in a separate process.

## Custom actions and logs

- **Deferred custom actions need HideTarget (0x2000) when they carry secrets.** `MsiHiddenProperties` hides
  property-change lines, but `Executing op: CustomActionSchedule(… CustomActionData=…)` still prints the data.
  Example: type 3073 → 11265.
- **Never return a raw `/l*v` log to an agent's context** without masking secrets first; logs are long and may
  contain passwords.

## Persistence, repair, uninstall

- **`AI_PersistentData` with `Installed` also fires on a real uninstall.** `OLDPRODUCTS OR Installed` makes repair
  safe but restores the backed-up file after uninstall cleanup. Use
  `OLDPRODUCTS OR (Installed AND NOT (REMOVE = "ALL"))`.
- **`RemoveFolders` leaves the install folder behind** when a custom action removed its children afterwards; remove
  it explicitly while empty.

## Services

- **`AI_ServiceConfigFailureActions` units are days (`ResetPeriod`) and minutes (`DelayActions`)**, not the seconds /
  milliseconds of the underlying Windows structure. `1` / `1` = reset after 1 day, restart after 60 s. Verify with
  `sc qfailure` or the service's `FailureActions` registry value.
- **`sc qfailure` does not list "no action" steps.** The registry blob is the source of truth.
- **`ServiceInstall.Dependencies`:** write `MSMQ` (or `MSMQ[~][OTHER_SERVICE]`); Advanced Installer adds the
  `[~][~]` terminator itself.

## Dialogs (UI)

- **An unchecked CheckBox leaves its property empty**, not `False`. If scripts treat "anything but False" as true,
  binding the box directly makes unchecking ineffective. Bind the box to a UI-only property and map it to
  `True` / `False` in the Test and Next button events.
- **Edit controls publish no change events.** A "test passed" flag cannot be reset when the user edits a field. Lock
  the fields after success (ControlCondition `Disable` on the flag, with an explicit unlock), or compare the current
  values with the tested ones in the Next condition.
- **The maintenance (Change) UI runs unelevated.** It cannot read files ACL'd to SYSTEM/Administrators; keep a
  non-secret copy of the values readable for the UI (e.g. HKLM) if pages must show the installed values.
- **Text controls do not grow.** A label that needs two lines in a control sized for one is clipped with "…". Size
  checks need the real font; a characters-times-width estimate is only a rough hint.
- **Ask for the result dialog style explicitly.** A result shown through an MSI dialog (icon + text + OK) looks like
  Windows Installer's own error dialogs, not like a Windows message box; teams may expect the latter for
  consistency with their other setups.
- **There is no UI under `/qn`.** Anything validated only by a dialog button (e.g. Test Connection) is not validated in
  a silent install unless an execute-sequence action repeats the check.

## Driving the real installer UI (what the runs had to learn)

- UI Automation exposes MSI controls as plain panes with no patterns.
- `WM_SETTEXT` changes the text but does not commit the property; the control must lose focus (or receive real key
  input).
- Each wizard page is a new window; find it again after every navigation.
- `PrintWindow` captures the setup window without bringing it to front but can drop labels; a real screen capture
  needs the window on top (and captures whatever else is in front otherwise).
