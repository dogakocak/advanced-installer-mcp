---
name: advanced-installer
description: Use when working on an Advanced Installer project (.aip file, AdvancedInstaller.com, MSI or EXE setup built by Advanced Installer) - building it, editing the project, setup dialogs and their text or layout, custom actions, Windows services, upgrades, or reading install logs.
---

# Advanced Installer

## Overview

An `.aip` is an XML project that Advanced Installer compiles into an MSI. Most of what goes wrong is invisible at
build time: the build succeeds while the package is wrong, a label is cut off, a secret lands in a log. **Verify the
built MSI and what the user sees, not the build's exit code.**

## Rules that cost the most when skipped

- **Never edit an `.aip` with `sed -i` from Git Bash.** `.aip` files are CRLF; Git Bash rewrites them as LF and
  Advanced Installer misbehaves. Use `AdvancedInstaller.com /edit`, PowerShell, or a script that keeps CRLF.
- **`ERROR: AI_ICE…` lines in build output are best-practice notes**, printed on clean builds too. They are not build
  errors; do not change the project because of them unless asked.
- **Exit code 0 does not mean the package is right.** Check the built MSI's tables (`File`, `Component`,
  `FeatureComponents`, `Control`, …) for what you changed.
- **Never put a raw verbose install log (`/l*v`) into your context.** Deferred custom actions print their data there;
  filter for what you need and check for secrets first.
- **Run `AdvancedInstaller.com` without a console** (redirected output). With a console it wraps long lines. Never
  run `AdvancedInstaller.com /help`: it opens the GUI and hangs.

## Reference (read the one you need)

| Task | File |
|---|---|
| Build, CLI, editing the `.aip`, verifying package content | `project-and-build.md` |
| Dialogs: adding controls, text that fits, conditions, seeing a dialog without installing (`show-dialog.ps1`) | `dialogs.md` |
| Custom actions, secrets, install logs | `custom-actions-and-logs.md` |
| Windows services, recovery settings, upgrades, repair, uninstall | `services-and-lifecycle.md` |

## When the advinst MCP tools are available

If tools named `build`, `lint_dialog` and `render_dialog` (server `advinst`) are in your tool list, use them:

| Instead of | Use |
|---|---|
| Running `AdvancedInstaller.com /build` and reading its output | `build` (structured result; `validation` holds the `AI_ICE` notes, `errors` the real failures; read `unrecognized`) |
| Estimating whether text fits from character counts | `lint_dialog` on the built MSI |
| Installing, driving the wizard and taking screenshots to see a dialog | `render_dialog` (pass `properties` for another state of the page) |

After a dialog change: `build` → `lint_dialog` on the returned package → fix findings → `render_dialog` to look at
the result. Install only when the task needs behaviour, not looks.
