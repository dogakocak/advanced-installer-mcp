# Measurement method

How we measure what an MCP server adds to an AI coding agent working on Advanced Installer projects. The same
method is used for the baseline and for every later arm, so numbers stay comparable.

## Arms

| Arm | Environment | When |
|---|---|---|
| A | Claude Code with shell tools (Bash/PowerShell) + a frozen project `CLAUDE.md` | baseline (done, see `2026-10-10-baseline-arm-a.md`) |
| B | A + a skill file with Advanced Installer knowledge, no MCP server | after the skill phase |
| C | MCP server + skill | after v0.1 |

`A − B` is what knowledge alone buys; `B − C` is what the MCP server itself buys. Reporting C against A alone would
credit the server with gains that a good skill file already delivers.

## Constants (kept identical across arms)

- Same project repository, same frozen starting commit per task, same prebuilt publish output; the run never
  republishes binaries.
- Same Advanced Installer version, same model, same Claude Code version (recorded per run).
- **Elevated terminal for every run.** An elevated session changes behaviour: the agent notices it can install and
  starts doing real install tests on its own (10+ minutes, millions of tokens). Elevation must not differ between arms.
- **Auto mode on** (permission prompts auto-approved). As a result the approval count is almost always 0 and the
  intervention count is the meaningful human-effort metric. Destructive actions are reviewed in the transcript.
- The advisor tool may be called by the agent on its own; the number of calls is recorded per run.
- **Skill (arms B and C):** the same `skills/advanced-installer` folder, copied from one recorded commit of this repo
  to `%USERPROFILE%\.claude\skills\advanced-installer` before the run and removed after it. Arm A runs have no skill
  installed. The skill's tool section applies only when the server's tools are present, so B and C read identical
  text and differ only in the tools.

## One run

1. A reset script puts the repository back to the frozen commit, removes build leftovers, restores the publish
   output, applies the task's precondition, checks the machine is clean (no installed product, no leftover files, no
   log containing the test password), verifies elevation, and copies the task prompt to the clipboard. It prints
   `READY`.
2. A **new** Claude Code session is started in the repository root.
3. The task prompt is pasted verbatim; no other context.
4. Wait for "done". Permission prompts: approve (counted). Content questions: shortest neutral answer (counted as an
   intervention). A destructive action is refused and noted.
5. Acceptance criteria are checked by the reviewer (scripts + by hand), metrics are recorded with the session id.
6. Leftovers (installed product, data folders, logs with secrets) are cleaned by the reviewer, not by the next run.

Three runs per task. A task whose prompt or precondition turns out to measure the wrong thing is versioned (`v2`)
and its earlier runs are kept but excluded from comparison.

**Rule learned the hard way:** while a run is active nobody touches the repository (no checkout, build or cleanup).
Check the newest transcript's last write time first.

## Metrics

| Metric | Definition | Source |
|---|---|---|
| First-try success | All acceptance criteria hold at the first "done" | criteria list |
| Attempts | Failed build/install calls + reverted edits | transcript |
| Tokens | `input`, `cache_creation_input`, `cache_read_input`, `output` summed separately. The JSONL writes one message on several lines: **deduplicate by `message.id`**, otherwise totals come out ~2× | `~/.claude/projects/<project>/<session>.jsonl` → `message.usage` |
| Log lines | Lines/characters of build and install output that reached the context | same JSONL |
| Approvals | Answers to permission prompts | transcript |
| Interventions | Real human messages after the first prompt | transcript |
| Duration | First prompt → "done" | JSONL timestamps |
| Scope | `—` in scope, `+` related extra changes, `++` unrequested fixes | diff review |

`tools/read_run_transcript.py` prints the deduplicated token totals, the time span and the tool-call flow of one
session.

## Reading the numbers

- Within one task, runs vary by **1.3–1.8×** in tokens in the baseline. A difference between arms counts only when
  it falls outside that band.
- Success and intervention were at the ceiling in the baseline (18/18, 0 interventions), so they can only detect
  regressions. Cost, duration and variance are where an improvement can show.
