# Arms B and C (2026-10-12 – 2026-10-14)

Arm B = arm A + the `skills/advanced-installer` skill, no MCP server. Arm C = arm B + this server (v0.1:
`lint_dialog`, `render_dialog`, `build`). Same subject project, tasks, prompts, starting commits and acceptance
criteria as the baseline (`2026-10-10-baseline-arm-a.md`); method in `measurement-method.md`.

- Skill: commit `2fc2bbd` in both arms (B and C read identical text).
- Server (C only): `1.0.0+b7ffe43`, pinned copy, local-scope registration on the subject repository.
- 18 valid runs per arm. Arm B had 3 invalid runs, re-run: one not elevated, one started with `/clear` instead of a
  new session, one with a full-screen game in front of the desktop (T6 captures the screen). Arm C had
  none.
- The T6 acceptance script was fixed between B and C (it now measures labels with GDI `DrawTextW`, written
  independently of `lint_dialog`, so arm C is not graded by its own tool); its earlier false positives on B runs had
  been overruled by the manual test.

## Results per task

All 36 runs passed at the first "done" with 0 interventions and 0 attempts. Duration is first prompt → last
assistant message.

| Task | Duration A | Duration B | Duration C | cache-read A | cache-read B | cache-read C | Reading |
|---|---|---|---|---|---|---|---|
| T1 version | 1:00–1:40 | 1:03–1:11 | 1:08–1:42 | 205K–314K | 271K–275K | 272K–336K | control task, no difference |
| T2 new file | 1:30–3:15 | 1:28–2:27 | 1:29–2:15 | 626K–842K | 634K–816K | 631K–767K | no difference |
| T3 service/recovery | 9:30–13:24 | 9:12–13:05 | 11:01–13:59 | 3.74M–6.63M | **3.17M–3.42M** | 3.58M–5.24M | skill gain: 0 attempts in B and C (A: 2); C's cost is scope (see below), the server has no tool for this task |
| T4 broken build | 1:41–2:04 | 1:16–1:42 | 1:22–1:40 | 455K–586K | 535K–676K | 420K–584K | no difference |
| T5 end-to-end install | 2:29–5:07 | 2:52–4:30 | 2:14–3:52 | 0.96M–1.37M | 1.01M–1.39M | 1.06M–1.19M | no difference; every run in every arm reported both product bugs |
| **T6 setup UI** | 20:41–29:31 | 14:20–24:21 | **10:28–16:26** | 12.24M–21.76M | 6.82M–16.40M | **4.34M–9.31M** | server gain, see below |

T6 medians: cache-read A 13.97M, B 9.73M, **C 4.52M**; duration A 26:00, B 16:14, **C 11:05**. C against B is about
2.2× cheaper, against A about 3.1×. The bands barely overlap (C's most expensive run is inside B's band, at its low
end), which is well outside the 1.3–1.8× run-to-run noise.

## Per run

Tokens = input / cache-write / cache-read / output, deduplicated by `message.id`. Advisor = calls by the agent.

| Run | Tokens B | Duration B | Advisor B | Tokens C | Duration C | Advisor C |
|---|---|---|---|---|---|---|
| T1-1 | 10 / 30.3K / 275K / 2.7K | 1:05 | 0 | 12 / 36.1K / 336K / 3.0K | 1:42 | 0 |
| T1-2 | 10 / 28.0K / 271K / 2.6K | 1:11 | 0 | 10 / 28.5K / 275K / 2.9K | 1:08 | 0 |
| T1-3 | 10 / 27.5K / 275K / 2.3K | 1:03 | 0 | 10 / 29.7K / 272K / 3.1K | 1:08 | 0 |
| T2-1 | 20 / 46.2K / 634K / 4.4K | 1:28 | 0 | 20 / 48.5K / 656K / 4.3K | 1:29 | 0 |
| T2-2 | 24 / 45.4K / 816K / 5.3K | 2:27 | 0 | 24 / 39.2K / 767K / 5.4K | 2:15 | 0 |
| T2-3 | 22 / 36.4K / 683K / 5.0K | 1:33 | 0 | 20 / 45.4K / 631K / 5.1K | 1:30 | 0 |
| T3-1 | 76 / 82.9K / 3.31M / 22.1K | 9:46 | 2 | 106 / 102K / 5.24M / 34.2K | 13:59 | 2 |
| T3-2 | 72 / 91.2K / 3.42M / 30.1K | 13:05 | 2 | 78 / 84.3K / 3.58M / 24.4K | 12:35 | 3 |
| T3-3 | 72 / 81.3K / 3.17M / 21.6K | 9:12 | 2 | 90 / 102K / 4.68M / 25.3K | 11:01 | 2 |
| T4-1 | 20 / 53.2K / 676K / 3.7K | 1:24 | 0 | 20 / 30.3K / 540K / 4.3K | 1:26 | 0 |
| T4-2 | 20 / 34.2K / 606K / 4.0K | 1:42 | 0 | 20 / 32.6K / 584K / 4.0K | 1:22 | 0 |
| T4-3 | 18 / 33.4K / 535K / 3.3K | 1:16 | 0 | 16 / 22.2K / 420K / 3.2K | 1:40 | 0 |
| T5-1 | 30 / 48.2K / 1.01M / 9.5K | 2:52 | 0 | 32 / 50.3K / 1.07M / 9.0K | 3:52 | 1 |
| T5-2 | 38 / 55.9K / 1.35M / 9.6K | 3:26 | 2 | 32 / 42.3K / 1.06M / 8.8K | 2:14 | 0 |
| T5-3 | 40 / 50.1K / 1.39M / 11.7K | 4:30 | 1 | 34 / 47.9K / 1.19M / 10.0K | 3:35 | 1 |
| T6-1 | 130 / 173K / 9.73M / 52.7K | 16:14 | 2 | 134 / 151K / 9.31M / 47.0K | 16:26 | 1 |
| T6-2 | 204 / 201K / 16.40M / 74.3K | 24:21 | 2 | 78 / 110K / 4.34M / 28.2K | 10:28 | 2 |
| T6-3 | 104 / 153K / 6.82M / 52.4K | 14:20 | 2 | 70 / 144K / 4.52M / 33.0K | 11:05 | 2 |

### T6 runs

| Run | How it checked the page | Server tool calls | Manual test |
|---|---|---|---|
| B-1 | own GDI measurement script; the skill's `show-dialog.ps1` (screen locked → switched a copy to `PrintWindow`); called the test script directly; one real silent install with the certificate check on | — | passed |
| B-2 | own GDI measurement script; `show-dialog.ps1`; then **drove the real setup wizard** with its own Win32 driver, installed, tested Change, uninstalled | — | passed |
| B-3 | own GDI measurement script; evaluated 14 Next-condition scenarios in a real MSI session; `show-dialog.ps1`; no install | — | passed |
| C-1 | `lint_dialog` found the clipped label; `render_dialog` for new install, error and Change; then **drove the real wizard** with its own Win32 driver and installed once to test Change | lint 6, render 4 | passed |
| C-2 | `lint_dialog` found the clipped label; `render_dialog` before test / after test / Change; test script against the database; **no wizard, no install** | lint 4, render 3 | passed |
| C-3 | same as C-2; said explicitly that `render_dialog` does not run custom actions and left the live flow to the manual test | lint 4, render 3 | passed |

Every T6 run in B and C found the pre-existing clipped label (in A only one of three did). In B each run wrote its
own GDI measurement for it; in C each run took `lint_dialog`'s finding and its suggested height ("20 units, now 18")
as given. Designs varied as in A: lock the fields after a successful test plus an "Edit Connection" button (B-1,
C-2, C-3), or compare the tested values in the Next conditions (B-2, B-3, C-1).

## Reading

- **The server pays off where the expensive work is: looking at and measuring dialogs.** In T6, `lint_dialog`
  replaced the measurement script B wrote every run, and `render_dialog` let two of three runs skip the real
  wizard entirely. Both of those runs passed the manual test, so render + lint + a direct call of the connection test
  were enough for this task.
- **The skill gives knowledge; the server changes the decision to verify the expensive way.** B had the same
  knowledge (units, pitfalls, a script that shows a dialog without installing) and still drove the real wizard in
  one run of three. In C that happened once as well (C-1): the variance shrank but did not disappear. A tool that
  drives the dialog (set a property, click, read the result) would cover what C-1 still did by hand.
- **T1–T5: the tools were never called** (15 runs). The subject project requires its own build script, which also
  verifies the built tables; one run loaded the `build` tool's schema and dropped it, quoting the project rule. This
  is the expected result for a project that wraps the CLI, not a failure of the tool; `build`'s value has to be
  measured on a project without a build script.
- **T3 in C cost more than in B** because all three C runs widened scope (fixed the configuration-restore bug, one
  also ran an upgrade test and fixed the password-in-log bug). The skill's gain (0 attempts) held.
- **Success and interventions stayed at the ceiling** in all arms (54/54, 0 interventions): no regression.

## Server bug found

T6 C-1 passed `null` as a property value to `render_dialog` (meaning "unset") and got "An error occurred invoking
'render_dialog'"; it retried without the key. Fixed after the series (the measured server stayed pinned): a null or
empty value now unsets the property, as an empty value does in Windows Installer.

## Limits

Three runs per task, one subject project, one machine, one model. T6's difference is larger than the noise band and
the arms' ranges barely overlap; for T1–T5 the arms are equal within noise.
