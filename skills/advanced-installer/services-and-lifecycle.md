# Services, upgrades, repair, uninstall

## Service recovery

- `AI_ServiceConfigFailureActions` uses **days** for `ResetPeriod` and **minutes** for `DelayActions`, not the
  seconds and milliseconds of the Windows structure. "Reset after 1 day, restart after 60 s" is `1` / `1`.
- Verify on an installed service with the registry value
  `HKLM\SYSTEM\CurrentControlSet\Services\<name>\FailureActions`. `sc qfailure` omits "no action" steps.

## Service dependencies

`ServiceInstall.Dependencies`: write `MSMQ`, or `MSMQ[~]OTHER_SERVICE` for several; Advanced Installer adds the
`[~][~]` terminator itself.

## Kept data and upgrades

- `AI_PersistentData` with the condition `Installed` also fires on a real uninstall, so a backed-up file is restored
  after cleanup deleted it. Use `OLDPRODUCTS OR (Installed AND NOT (REMOVE = "ALL"))` for "upgrade or repair, not
  uninstall".
- `RemoveFolders` leaves the install folder behind when a custom action removed its contents afterwards; remove the
  folder explicitly once empty.

## Testing install behaviour

An elevated terminal can install for real. A full install / change / uninstall cycle takes minutes and fills the
context with output; do it when the task asks about behaviour, and leave the machine clean (product uninstalled,
data folders and logs removed).
