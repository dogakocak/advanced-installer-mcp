# ADR 0008 — mock_dialog: proposed setup pages as real MSI rows, shown before the project changes

- Date: 2026-11-05
- Status: Accepted

## Context

v0.1/v0.2 check setup pages **after** they are built: `lint_dialog`, `render_dialog` and `drive_dialog` all read a
built MSI. When an agent adds or redesigns pages, the expensive mistakes happen before that, in the design: a free
text field where two fixed values call for radio buttons, a check box whose unchecked state nobody thought about, a
page that cannot be filled from the command line, a label that does not fit. Today the user sees the result only
after the `.aip` was edited and built, and a wrong page means undoing project edits.

Two ways to show a page before building it:

- **A hand-drawn picture** (HTML mock-up, as people already ask agents for). It looks good, but nothing stops it from
  showing what Windows Installer cannot draw (custom widgets, free layout, web fonts), so "approved mock" and "built
  page" drift apart.
- **Real rows, drawn by the calibrated renderer.** `DialogRenderer` already draws exactly the Windows Installer
  control types, with the package's fonts, measured as msiexec measures them (ADR 0003, ADR 0006). It needs a
  `DialogPackage`, i.e. an MSI database. The test suite already creates small MSIs through msi.dll
  (`TestPackage`), so a page that exists only as data can become a throw-away MSI in milliseconds.

Scope is the classic Dialog-table UI. A package with an embedded UI (`MsiEmbeddedUI`, Advanced Installer's Enhanced
UI themes) shows something else; `lint_dialog` already warns about it (`embedded-ui`, ADR 0005).

## Decision

A new tool, **`mock_dialog`**, takes a spec of proposed pages, writes it into a temporary MSI, draws every page with
`render_dialog`'s painter, runs `lint_dialog`'s rules on it, and returns the pictures, the findings and an approval
page for the user. Nothing is installed and no project is touched.

### Spec: table rows, not a widget language

The spec mirrors the Windows Installer tables, with their column names:

```json
{
  "properties": { "CERT_MODE": "pfx" },
  "textStyles": [ { "name": "Title", "faceName": "Segoe UI", "size": 12, "styleBits": 1 } ],
  "dialogs": [
    {
      "name": "CertificateDlg",
      "description": "Where the service gets its TLS certificate.",
      "frame": "wizard",
      "controls": [
        { "name": "ModeGroup", "type": "RadioButtonGroup", "x": 20, "y": 60, "width": 330, "height": 40,
          "property": "CERT_MODE", "rationale": "Two fixed choices.",
          "items": [ { "value": "pfx", "text": "&PFX file" }, { "value": "store", "text": "Windows certificate &store" } ] },
        { "name": "PfxPath", "type": "PathEdit", "x": 20, "y": 110, "width": 260, "height": 18,
          "property": "CERT_PFX_PATH", "rationale": "A file path." }
      ],
      "conditions": [ { "control": "PfxPath", "action": "Disable", "condition": "CERT_MODE <> \"pfx\"" } ]
    }
  ]
}
```

- `properties`, `textStyles`, `dialogs[].controls` and `conditions` are rows of `Property`, `TextStyle`, `Dialog`,
  `Control` and `ControlCondition`. `items` are the `RadioButton` rows of a `RadioButtonGroup` (stacked one per line
  when a row gives no position) or the `ComboBox` / `ListBox` rows of a `ComboBox` / `ListBox`. Attributes are the documented
  numbers, as in the tables.
- Keeping the rows means the realism guarantee is a validation step, not a translation, and a later comparison of
  spec and built package is row against row.
- Control `next` may be omitted: the tab order is then the order of `controls`, followed by the frame's buttons.
- `description` (per dialog) and `rationale` (per control) are not table data; they go only on the approval page.

### Frame and defaults from Advanced Installer, not invented

`"frame": "wizard"` (the default) adds the rows every Advanced Installer wizard page has: banner bitmap and title
texts, the separator lines, Back/Next/Cancel, and `Control_First`/`Default`/`Cancel`. `"frame": "none"` adds
nothing. The frame rows, the dialog size (370 × 270), `DefaultUIFont` and the default `TextStyle` rows are copied
from a package Advanced Installer 24.0 builds from a new project, and kept as a test fixture with the source noted.
A frame that differs from Advanced Installer's would make every approved page differ from the built one in rows the
user never chose.

### Validation (the realism guarantee)

Refused with an `McpException` that says what is allowed:

- a control `type` that is not a Windows Installer control type;
- names that are not Identifiers (dialogs, controls, properties, text styles), duplicate names, an `items` list on a
  type that has none;
- an input control (Edit, MaskedEdit, PathEdit, CheckBox, RadioButtonGroup, ComboBox, ListBox) bound to a property
  with lowercase letters: a private property cannot be set on the `msiexec` command line and never reaches the
  execute sequence (ADR 0009);
- a control outside the dialog is not refused: it is a `lint_dialog` finding, so the user sees it on the page.

Types the renderer does not draw (feature tree, volume lists, progress bar, billboard) are accepted and drawn as
labelled frames, as `render_dialog` does.

### Tool contract

`mock_dialog(spec, dialog?, scaling = 100, properties?, outputDir?)`

- `dialog` omitted = every page, as in `lint_dialog`; `properties` overrides values for another state of a page, as
  in `render_dialog`.
- Returns one image block per page and a JSON report: the `lint_dialog` findings (layout, text fit, and ADR 0009's
  rules that apply without an execute sequence) plus `render_dialog`'s report per page.
- With `outputDir`: writes `<Dialog>.png` per page and **`index.html`, the approval page** (below), and returns the
  path. Without it the tool only returns the images to the agent.
- The temporary MSI lives in `%TEMP%` and is deleted in `finally`. It needs no ProductCode or summary stream: render
  and lint only read the database.
- Annotations: `ReadOnly = false` (with `outputDir` it writes files), `Destructive = false`, `Idempotent = true`,
  `OpenWorld = false`.

### Approval page

One card per page, generated from the spec so its format never varies and the agent spends no tokens on HTML:

- the picture and the dialog's `description`;
- an input table: control type, property, default, `rationale`, and the command-line form for every value
  (`CERT_MODE=pfx`, `CERT_MODE=store`; a check box as `PROP=1` and "omit for unchecked");
- the page's findings;
- at the end, one `msiexec /i <package> /qn ...` line with every input property, so the unattended form is part of
  what the user approves.

The skill already has this gate (`setup-ui-review.md`, 2026-10-26): every screen as a wireframe drawn with
`wireframe-template.html`, an input table, the unattended command line, approval before the `.aip` changes. With the
`advinst` tools present the skill uses `mock_dialog`'s approval page instead of the hand-filled wireframe; without
them the wireframe stays, so the skill still works on its own.

### Writer moves into the server

The MSI writer moves from the test fixture into `src/` (table creation and row inserts for the tables above).
`TestPackage` becomes a thin wrapper over it, so tests and tool write packages the same way. The server no longer
only reads packages; `MsiDatabase`'s comment and `Create`/`Execute`/`Commit` visibility change accordingly.

## Alternatives considered

- **HTML mock-ups drawn by the agent.** No guarantee that only Windows Installer controls appear; fonts and sizes are
  guesses, so text fit cannot be checked; each mock costs tokens and looks different.
- **Skill only: edit the `.aip`, `build`, `render_dialog`.** No new code, but the project is changed before the user
  approved anything, which defeats the gate, and every iteration costs a build. The measurement also argues for a
  tool: on the UI task the skill-only arm (B) improved on the baseline but varied widely (6.8–16.4M cache-read
  tokens), while the arm with tools (C) was cheaper and steadier (median 4.5M) (`docs/research/2026-10-14-arms-b-and-c.md`).
- **A widget-level spec** (`{"type": "choice", "options": [...]}` mapped to controls by the tool). Shorter specs, but
  the tool would pick controls, sizes and positions, a second mapping would hide the real rows, and the comparison
  with the built package would compare against a translation.
- **Extend `render_dialog` to accept a spec.** One tool fewer, but mixes "look at this built package" with "propose
  pages"; the approval page and validation belong to the second only.

## Consequences

- One new tool, one new writer surface in `src/`, a frame fixture taken from Advanced Installer 24.0.
- The renderer draws a `ComboBox`'s current value; with `ComboBox` rows it should show the item's text instead. Small
  change, needed for mocks and for built packages alike.
- What the user approves is rows; after the build, `lint_dialog` and `render_dialog` on the real package show whether
  it matches, side by side with the approved picture.

## Open points

- **Spec ↔ built package comparison** (row diff of `Control`, `RadioButton`, `ComboBox`, `ControlCondition`): left
  for a later version. The row-shaped spec keeps it a plain comparison.
- **Events** (`ControlEvent` rows in the spec, so `drive_dialog` could play a mock): not in v0.3. Pages are approved
  for look and inputs; behaviour is checked with `drive_dialog` after the build.
- Text in the spec is taken as written; localisation (`ValueLocId`) is out of scope.
