# Custom actions and logs

## Secrets

- **A deferred custom action that receives a secret needs the HideTarget flag (0x2000)** in its `CustomAction.Type`,
  e.g. 3073 → 11265. `MsiHiddenProperties` hides the property lines, but the verbose log still prints
  `Executing op: CustomActionSchedule(… CustomActionData=…)` with the data in clear text.
- Check: install with `/l*v`, then search the log for the secret value. Do not print matching lines; count them.

## Logs

- Verbose logs are long and may contain secrets. Never read one whole into your context. Search for what you need:
  `Return value 3` (first failure), the custom action's name, `Property(S): NAME`.
- Write the log to a path you control and delete it after the check.

## Scheduling

- A custom action only in `InstallUISequence` does not run in a silent install (`AI_ICE05` says so). If the action
  must always run, also schedule it in `InstallExecuteSequence`.
- Deferred actions see only `CustomActionData`, not public properties.
