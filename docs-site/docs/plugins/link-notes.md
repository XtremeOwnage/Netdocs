---
title: link-notes
---

# link-notes

Automatically attaches a **note** (arbitrary markdown) to outbound links that match a
rule, so that:

- hovering the link shows the note as a **tooltip**, and
- the note is rendered **once at the bottom of the page** (the footer),

without you having to hand-add a footnote to every post. It is a first-party Netdocs plugin
— there is no equivalent MkDocs plugin — and is fully opt-in and data-driven.

!!! info "Formerly `affiliate-links`"
    This plugin was renamed from `affiliate-links` to the neutral `link-notes` (attaching a
    disclosure to affiliate links is just one use-case). The old name still works as an
    **alias**, and the legacy `programs` / `disclosure` config keys are still accepted — no
    changes are required to existing configs.

## Two render modes: footnote or hover popup

Each rule renders its note in one of two ways:

- **Footnote mode** (default) — the link gets a small footnote marker; the note appears both as a
  hover tooltip (with Material's `content.footnote.tooltips`) and once in the page's footnote list.
- **Tooltip mode** (`link_snippet` set) — each matching link is *replaced inline* with a snippet
  rendered as a template (a hover popup), and the rule's disclosure box is emitted **once per page**
  instead of per-link footnotes. See [Pretty hover popups](#pretty-hover-popups-tooltip-mode) below.

## A common use-case: affiliate disclosures

Attaching an affiliate-disclosure note to eBay Partner Network / tagged Amazon links means
the disclosure appears on hover **and** once in the page footer, satisfying the
once-per-page disclosure requirement automatically:

```json
{
  "name": "link-notes",
  "options": {
    "rules": [
      {
        "name": "ebay",
        "label": "Affiliate links",
        "domains": [ "ebay.us" ],
        "note": "This is an eBay Partner Network affiliate link. It costs you nothing extra, and purchases made through it help support this site."
      }
    ]
  }
}
```

A link such as `[HBA card](https://ebay.us/abc123)` becomes, in the rendered page, a link
with a small footnote marker; hovering it reveals the note, and the same text appears in the
page's footnote list.

!!! tip "Just write a normal link"
    You do **not** need a snippet or macro. Prefer an ordinary Markdown link —
    `[used APC PDU](https://ebay.us/bSAxHF)` — and let this plugin add the note. That keeps
    posts readable and avoids per-link markup such as
    `--8<-- "ebay.html" text="..." url="..."`, which is noisier and easy to get wrong.

## Reusing a snippet for the note

Instead of typing the note text inline in your config, a rule can point at a Markdown
**snippet file** with `note_snippet`. This keeps a single source of truth for a disclosure
that you may also include manually elsewhere (via [snippets](snippets.md)), so the wording
stays consistent everywhere:

```json
{
  "name": "ebay",
  "domains": [ "ebay.us" ],
  "note_snippet": "snippets/ebay-affiliate.md"
}
```

The path is resolved against the project root and the `docs/` directory, each with a
conventional `snippets` subdirectory — so `snippets/ebay-affiliate.md`,
`docs/snippets/ebay-affiliate.md`, or a bare `ebay-affiliate.md` (found in `docs/snippets`)
all work. If a referenced snippet **cannot be found, the build fails** with a clear error
(even without `--strict`) — a mistyped path can never silently drop an affiliate disclosure.

When the snippet is a single **admonition** — the usual pretty affiliate box:

```markdown
!!! info "E-Bay Affiliate Links Used"
    This post **DOES** include eBay affiliate links. ...

    You will pay the same amount as normal ...
```

…the plugin is admonition-aware:

- its **title** (`E-Bay Affiliate Links Used`) becomes the standalone fallback box's header
  (unless the rule sets an explicit `label`), and its admonition **kind** (`info`) is reused;
- its **body** becomes the tooltip / footer-note text. (An admonition can't render *inside*
  a footnote, so the body is used directly there; the pretty box is reproduced for
  table-only links via the standalone admonition.)

## Pretty hover popups (tooltip mode)

Footnotes are great for a plain disclosure, but sometimes you want a *nicer* per-link popup — a
styled card that appears on hover — instead of a superscript number and a footnote list. Point a
rule at a **`link_snippet`** to switch it into tooltip mode:

```json
{
  "name": "ebay",
  "domains": [ "ebay.us" ],
  "link_snippet": "snippets/ebay-link.html",
  "note_snippet": "snippets/ebay-affiliate.md"
}
```

In this mode, **every matching link is replaced inline** with `link_snippet` rendered as a template,
and the `note_snippet` disclosure box is emitted **once at the bottom of the page** (no per-link
footnotes at all). The snippet receives the matched link as template parameters, using the same
`${key}` convention as parameterized [snippets](snippets.md) includes:

| Placeholder | Value |
|---|---|
| `${url}` | The matched link URL (HTML-escaped). |
| `${text}` | The link's display text (HTML-escaped). |
| `${domain}` | The URL host, e.g. `ebay.us` (HTML-escaped). |

A `link_snippet` is typically a small HTML fragment that wraps the link with a CSS-styled tooltip:

```html
<span class="affiliate-wrapper"><a href="${url}" target="_blank" rel="nofollow sponsored noopener"
class="affiliate-link">${text}</a><span class="affiliate-tooltip"><span class="tooltip-title">eBay
Affiliate Link</span><span class="tooltip-content">This is an eBay affiliate link…</span></span></span>
```

Because the replacement is inline HTML (not a footnote), tooltip mode also works **inside pipe-table
cells** — where footnote references can't go — so links generated from CSVs by the
[table-reader](table-reader.md) get the same pretty popup. A referenced-but-missing `link_snippet`
**fails the build**, exactly like `note_snippet`.

!!! tip "Style it once"
    Put the tooltip CSS (`.affiliate-wrapper` / `.affiliate-tooltip` etc.) in your `extra_css` and
    reuse the same classes across every affiliate snippet so all popups look consistent.

## How it works

The plugin runs as a Markdown preprocessor (order `30`, after
[snippets](snippets.md), [table-reader](table-reader.md) and [macros](macros.md), so links
those plugins generate are also covered). For every configured *rule* it scans the page for
links that match by **domain** and/or **regular expression** and appends a footnote
reference carrying the note text. Because the tooltip and the footer note both come from the
same footnote, enabling the Material
[`content.footnote.tooltips`](../reference/theme.md) feature gives you the hover behavior for
free.

## Matching by domain and query parameter

Some links only qualify when they carry a specific query parameter — for example a raw
`amazon.com` URL is only an affiliate link when it has a `tag=` parameter, while `amzn.to`
short links always are. A domain entry can therefore be either a plain string or an object
with its own `query_contains` marker:

```json
{
  "name": "amazon",
  "label": "Affiliate links",
  "domains": [
    "amzn.to",
    { "domain": "amazon.com", "query_contains": "tag=" }
  ],
  "note": "This is an Amazon affiliate link. As an Amazon Associate I earn from qualifying purchases at no additional cost to you; it helps support this site."
}
```

A rule-level `query_contains` may also be set; it applies to every plain-string domain that
doesn't override it. Subdomains of a configured domain match automatically.

## Matching by regular expression

For anything a domain rule can't express, add a `patterns` list. Each entry is a regular
expression matched **case-insensitively against the full URL**; a link matches the rule if
**any** pattern (or any domain rule) matches:

```json
{
  "name": "sponsored",
  "label": "Sponsored",
  "patterns": [
    "https?://[^/]*/go/",
    "utm_source=sponsor"
  ],
  "note": "This is a sponsored link."
}
```

Invalid patterns are logged and skipped rather than aborting the build. A rule needs at
least one `domains` entry or one `patterns` entry; a rule with neither is dropped with a
warning.

## What is left untouched

To keep output valid, the plugin does **not** inject a footnote reference when:

- the link **already carries a footnote** (e.g. a hand-authored `[^ebay]`), so it coexists
  with existing content during migration;
- the link is inside a **fenced code block**;
- the link is inside a **pipe-table cell** (Markdig can't reliably parse footnote references
  there); or
- the link is **glued directly to an adjacent link** (`[a](x)[b](y)`), where a wedged
  footnote would render ambiguously.

In the table-cell and adjacent-link cases the footer note is still guaranteed: the rule
emits a standalone `!!! info "<label>"` admonition at the bottom of the page instead of a
footnote.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `rules` | array | — | The link rules to detect (see below). Legacy alias: `programs`. |

Each **rule** object:

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Rule id; used to build the footnote label (`linknote-<name>`). |
| `note` | string | yes* | Markdown shown as the tooltip and footer note. Legacy alias: `disclosure`. |
| `note_snippet` | string | yes* | Path to a Markdown snippet whose content is used as the note (resolved against the project root / `docs` dir and their `snippets` subdirs). A single-admonition snippet contributes its title (→ `label`) and kind, and its body becomes the note. A referenced-but-missing snippet **fails the build**. Legacy alias: `disclosure_snippet`. |
| `link_snippet` | string | no | Path to an HTML/Markdown snippet template. When set the rule switches to **tooltip mode**: each matching link is replaced inline with this snippet rendered with `${url}`/`${text}`/`${domain}` substituted, and the `note`/`note_snippet` box is emitted once per page (no footnotes). A referenced-but-missing snippet **fails the build**. |
| `domains` | array | no† | Hosts that identify the link. Each entry is a domain string or `{ "domain": "...", "query_contains": "..." }`. Subdomains match automatically. |
| `patterns` | array | no† | Regular expressions matched (case-insensitively) against the full URL. |
| `query_contains` | string | no | Default substring a matching URL must contain (per-domain values override this). |
| `label` | string | no | Title for the standalone fallback admonition (table-only links). Defaults to the snippet's admonition title, else `Links`. |

\* A rule must provide either a note (`note` or `note_snippet`) or a `link_snippet`. If `note_snippet`
is set it takes precedence over `note`; any referenced snippet that cannot be found fails the build
rather than falling back, so a mistyped path is caught instead of silently dropping the note.

† A rule must provide at least one of `domains` or `patterns`.

!!! tip
    Enable [`content.footnote.tooltips`](../reference/theme.md) in your theme `features`
    so the notes appear as hover tooltips as well as in the footer.
