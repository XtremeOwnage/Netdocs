---
title: calculator
---

# calculator

Author an **interactive calculator** entirely in Markdown. A fenced <code>```calc</code>
block written in YAML becomes a small client-side form: labelled inputs plus outputs that
recompute live as you type. No server, no build-time math — just a tiny vanilla-JS evaluator.

It is a first-party Netdocs plugin (there is no MkDocs equivalent). Enable it in `plugins`:

```json title="appsettings.json"
{ "name": "calculator" }
```

## Example

````markdown
```calc
title: Power cost
inputs:
  - name: watts
    label: Power draw (W)
    default: 100
  - name: hours
    label: Hours per day
    default: 24
  - name: rate
    label: Price ($/kWh)
    default: 0.12
    step: 0.01
outputs:
  - label: Daily cost
    expr: (watts * hours / 1000) * rate
    format: "$0.00"
  - label: Monthly cost
    expr: (watts * hours / 1000) * rate * 30
    format: "$0.00"
```
````

Renders as:

```calc
title: Power cost
inputs:
  - name: watts
    label: Power draw (W)
    default: 100
  - name: hours
    label: Hours per day
    default: 24
  - name: rate
    label: Price ($/kWh)
    default: 0.12
    step: 0.01
outputs:
  - label: Daily cost
    expr: (watts * hours / 1000) * rate
    format: "$0.00"
  - label: Monthly cost
    expr: (watts * hours / 1000) * rate * 30
    format: "$0.00"
```

## How it works

The plugin runs as a Markdown preprocessor (order `15`) and replaces each `calc` fence with
a raw-HTML `<form class="nd-calc">`. A single evaluator script is injected **once per page**;
it collects each form's inputs into named variables, evaluates every output expression, and
updates the results on every `input`/`change`. It also re-binds on Material's `document$`
observable, so calculators keep working with instant navigation.

## Inputs

Each entry under `inputs`:

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Variable name used in expressions. Must be a valid identifier (`[A-Za-z_][A-Za-z0-9_]*`). |
| `label` | string | no | Field label (defaults to `name`). |
| `type` | string | no | `number` (default), `range`, or `select`. Providing `options` implies `select`. |
| `default` | number/string | no | Initial value. |
| `min` / `max` / `step` | number | no | Passed through to the `<input>` (useful for `number`/`range`). |
| `options` | array | no | For selects — a list of strings or `{ value, label }` objects. |

A `range` input also renders a small live readout of its current value.

## Outputs

Each entry under `outputs`:

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | no | Result label (defaults to `Result`). |
| `expr` | string | yes | Expression evaluated with the input names as variables. |
| `format` | string | no | Number format (see below). |

### Expressions

Expressions are evaluated in the browser with the inputs available as variables and the
JavaScript [`Math`](https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/Math)
object in scope, so `Math.round`, `Math.min`, `Math.pow`, ternaries (`a > b ? x : y`), etc.
all work:

```yaml
outputs:
  - label: Rounded
    expr: Math.round(watts * hours / 1000)
```

!!! warning "Sanitised input"
    Expressions are restricted to a safe character set (digits, identifiers, arithmetic and
    comparison operators, parentheses, commas). Quotes, semicolons, brackets and backslashes
    are rejected, and such an output is dropped with a build warning — so a stray expression
    can't inject arbitrary script into the page.

### Formats

`format` is a lightweight pattern: the first `0` / `0.0…` token sets the number of decimal
places, and any surrounding text is kept as a prefix/suffix.

| `format` | `1234.5` renders as |
|---|---|
| *(omitted)* | `1234.5` |
| `0` | `1235` |
| `0.00` | `1234.50` |
| `$0.00` | `$1234.50` |
| `0.0 kWh` | `1234.5 kWh` |
