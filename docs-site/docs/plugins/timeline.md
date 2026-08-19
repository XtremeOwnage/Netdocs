---
title: timeline
---

# timeline

Author an **interactive project timeline** entirely in Markdown. A fenced
<code>```timeline</code> block written in YAML declares a chain of named, offset dates —
calendar days or business days, with optional holiday/blackout exclusions — and renders as a
date picker per anchor date plus a Mermaid `gantt` diagram that recomputes live as you change
a date. Like [calculator](calculator.md), all of the math happens client-side — no server,
no rebuild.

It is a first-party Netdocs plugin (there is no MkDocs equivalent). Enable it in `plugins`:

```json title="appsettings.json"
{ "name": "timeline" }
```

## Example

````markdown
```timeline
title: Communications
date_format: MM/DD/YYYY
show_summary: true
display_date_format: "dddd MMM dd, yyyy"
edit_exclusions: true
inputs:
  - name: start
    label: Project Start
    default: 01/01/2027
outputs:
  - name: first
    label: Project Announcement Email
    type: all
    expr: start
    duration: 1
  - name: second
    label: Kick Off Meeting
    type: weekdays
    expr: first + 1
    duration: 1
  - name: sprint1
    label: Sprint 1
    type: weekdays
    expr: second + 1
    duration: 10
  - name: sprint2
    label: Sprint 2
    type: weekdays
    expr: sprint1 + 1
    duration: 10
  - name: projectclosure
    label: Project Closure Meeting
    type: weekdays
    expr: sprint2 + 1
    duration: 1
```
````

Renders as:

```timeline
title: Communications
date_format: MM/DD/YYYY
show_summary: true
display_date_format: "dddd MMM dd, yyyy"
edit_exclusions: true
inputs:
  - name: start
    label: Project Start
    default: 01/01/2027
outputs:
  - name: first
    label: Project Announcement Email
    type: all
    expr: start
    duration: 1
  - name: second
    label: Kick Off Meeting
    type: weekdays
    expr: first + 1
    duration: 1
  - name: sprint1
    label: Sprint 1
    type: weekdays
    expr: second + 1
    duration: 10
  - name: sprint2
    label: Sprint 2
    type: weekdays
    expr: sprint1 + 1
    duration: 10
  - name: projectclosure
    label: Project Closure Meeting
    type: weekdays
    expr: sprint2 + 1
    duration: 1
```

Change the **Project Start** picker above — the whole chain redraws immediately: each sprint's
bar starts the weekday after the previous one *ends*, not the day it started. Since
`edit_exclusions` is on, try adding a holiday in the middle of Sprint 1 too — its end date (and
everything after it) shifts to absorb the extra day.

## How it works

The plugin runs as a Markdown preprocessor (order `15`, same tier as
[calculator](calculator.md)) and rewrites each `timeline` fence into raw HTML: one
`<input type="date">` per `inputs[]` entry, an empty container for the diagram, and a JSON
"spec" of the whole output graph (names, labels, types, and each `expr` already parsed into a
`{ref, direction, count}` triple). None of that changes after the page builds.

A site-wide script (registered once, not per-diagram) reads that spec, and on page load *and*
every time an input changes: re-derives every output date from the current input values (in
declaration order — an `expr` may only reference a name defined earlier in the same block),
rebuilds the mermaid source text, and renders it via a lazily-imported Mermaid straight into the
page — no server round-trip. Date arithmetic runs on a plain day-count algorithm rather than
JavaScript's `Date`, which sidesteps that type's timezone-shift quirks entirely.

Every milestone gets a real slot on the date axis (`dateFormat`/`axisFormat`); the generated
source also sizes the font and picks a `tickInterval` from the actual date span so a short
project doesn't get padded out to an unreadably wide axis.

## Inputs

Every field below is a direct top-level key — `title`, `date_format`, `exclusions`,
`show_summary`, `display_date_format`, `edit_exclusions`, `inputs`, `outputs` are all siblings.
There's no nested settings object to learn as a second rule.

| Field | Type | Default | Required | Description |
|---|---|---|---|---|
| `title` | string | — | no | Diagram title and section name. |
| `date_format` | string | `MM/DD/YYYY` | no | Token format for every date *authored* in this block (`default`, `exclusions`). |
| `exclusions` | array | — | no | Dates that never count as a landing day — see [Exclusions](#exclusions). |
| `show_summary` | boolean | `true` | no | Show the exact-date table below the diagram, sorted chronologically regardless of declaration order (the diagram's own row order stays grouped by input/output as declared). |
| `display_date_format` | string | `dddd MMM dd, yyyy` | no | How dates are *displayed* in that table and the exclusions list — see [Display date format](#display-date-format). |
| `edit_exclusions` | boolean | `false` | no | Let the reader add/remove excluded dates in the page itself — see [Editing exclusions](#editing-exclusions). |
| `inputs` | array | — | yes | The anchor date(s) everything else is computed from. |
| `outputs` | array | — | yes | Named, computed dates. |

### `inputs[]`

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Reference name used in `outputs[].expr`. Must be a valid identifier (`[A-Za-z_][A-Za-z0-9_]*`). |
| `label` | string | no | Milestone/bar label (defaults to `name`). |
| `default` | string | yes | Initial value of the date picker, parsed with `date_format`. |
| `type` | string | no | `all` (default) or `weekdays` — which days count while stepping through `duration`. |
| `duration` | integer | no | Whole days, default `0` — see [Duration and end dates](#duration-and-end-dates). |
| `editable` | boolean | no | Default `true` — a date picker the reader can change. `false` renders `default` as plain text instead (formatted with `display_date_format`, not a native picker's browser-locale format) — still a full graph root: `duration` and `outputs[].expr` both still work, it just can't be moved in the page. |

### `outputs[]`

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Reference name, usable by later outputs. Must be a valid identifier and unique across the block. |
| `label` | string | no | Milestone/bar label (defaults to `name`). |
| `type` | string | no | `all` (default, every calendar day counts) or `weekdays` (Saturday/Sunday are skipped). Governs both the `expr` offset and `duration`. |
| `expr` | string | yes | `<ref>`, `<ref> + N`, or `<ref> - N` — see [Expressions](#expressions). |
| `duration` | integer | no | Whole days, default `0` — see [Duration and end dates](#duration-and-end-dates). |

### Expressions

An `expr` is a reference to an earlier `inputs`/`outputs` name, optionally offset by a whole
number of days:

```yaml
expr: start          # same date as start
expr: start + 5
expr: renewal - 10
```

The offset is counted in whatever unit that *output's own* `type` declares — `all` steps every
calendar day; `weekdays` steps only Monday–Friday. An `expr` that references an unknown name, or
one declared later in the same block, is dropped with a build warning rather than failing the
whole page.

!!! note "Offsets are capped at 100000 steps"
    That is roughly 274 years, so no real schedule reaches it. The evaluator stops stepping there,
    and an offset beyond it would silently resolve to whatever date the evaluator had reached — so
    it is reported as a build warning and the output is dropped, rather than published as a date
    that looks right and isn't.

### Diagram size

The gantt is drawn at the width of the block it sits in, so its labels render at their intended
size rather than being scaled down to fit. It is never drawn narrower than **720px**, though: below
that a gantt starts overlapping its own task names and axis ticks. On a narrow screen the diagram
keeps its readable size and scrolls horizontally instead, and the exact-date table underneath stays
fully readable either way. Resizing the window redraws it at the new width.

### Duration and end dates

Every input and output has a `duration` (whole days, default `0`), stepped the same way an
`expr` offset is — honouring that item's own `type` and any `exclusions`. That produces two
dates per item: a **start** (from `default`, the date picker, or an `expr`) and an **end**
(start stepped forward by `duration`).

**`duration: 0` renders as a milestone** — a single point on the axis. **A nonzero `duration`
renders as a task bar** spanning start to end.

When an `expr` references an earlier name, it chains off that name's **end**, not its start —
"the next thing begins when this one finishes," not when it started:

```yaml
- name: sprint1
  duration: 10
  expr: kickoff + 1   # starts the weekday after kickoff *ends*
```

If `kickoff` has `duration: 0` (a milestone), its start and end are the same date, so this
behaves exactly like before duration existed. It only changes behavior once something upstream
actually has a span to finish.

### Exclusions

`exclusions` lists dates that are never counted as a landing day, regardless of an output's
`type` — the natural place for holidays or office closures. Every date here — a bare scalar or
either side of a `{ from, to }` range — is parsed with the same top-level `date_format` as
`inputs[].default`; there's one shared format for every date authored in the block, not a
separate one per section:

```yaml
exclusions:
  - 12/25/2027
  - { from: 12/24/2027, to: 12/26/2027 }   # inclusive range
```

A `weekdays` output skips weekends *and* exclusions; an `all` output skips only exclusions.
Concretely, `expr: start + 1` with `type: weekdays` means "the next weekday that isn't an
exclusion" — if that lands on a holiday, it keeps stepping forward (or backward, for `- N`)
until it finds one that isn't.

!!! warning "Exclusion ranges are capped"
    A `{ from, to }` range is expanded eagerly and capped at 366 days; a longer range logs a
    warning and is truncated.

### Display date format

`display_date_format` is display-only and unrelated to the top-level `date_format` (which
parses dates *authored* in the YAML) — two fields, one job each, distinct names on purpose so
neither can be mistaken for the other. It controls how dates render in the summary table and
the exclusions list, using the same tokens as .NET's custom date format strings — `d`/`dd` for
day-of-month, `ddd`/`dddd` for weekday name, `M`/`MM`/`MMM`/`MMMM` for month, `yy`/`yyyy` for
year (repeat count controls padding vs. name):

| `display_date_format` | `2027-01-06` renders as |
|---|---|
| `dddd MMM dd, yyyy` *(default)* | `Wednesday Jan 06, 2027` |
| `MM/dd/yyyy` | `01/06/2027` |
| `MMMM d, yyyy` | `January 6, 2027` |
| `yyyy-MM-dd` | `2027-01-06` |

### Editing exclusions

With `edit_exclusions: true`, the excluded-dates list (shown under the inputs, above the
diagram) becomes interactive — each date gets a **✕** to remove it, and a date picker lets the
reader add new ones. Both take effect immediately, exactly like changing an input: everything
downstream that steps through the affected `type: weekdays` (or matches the added/removed
exclusion) recomputes.

```timeline
title: Communications
edit_exclusions: true
inputs:
  - name: start
    label: Project Start
    default: 01/01/2027
exclusions:
  - 01/18/2027
outputs:
  - name: sprint1
    label: Sprint 1
    type: weekdays
    expr: start + 1
    duration: 10
```

Without `edit_exclusions` (the default), the list still shows whenever `exclusions` is
non-empty — just without the ✕ buttons or the add control. It's hidden entirely only when
there's nothing to say: no exclusions *and* editing is off.
