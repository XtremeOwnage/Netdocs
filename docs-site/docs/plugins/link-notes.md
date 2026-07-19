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
| `note` | string | yes | Markdown shown as the tooltip and footer note. Legacy alias: `disclosure`. |
| `domains` | array | no* | Hosts that identify the link. Each entry is a domain string or `{ "domain": "...", "query_contains": "..." }`. Subdomains match automatically. |
| `patterns` | array | no* | Regular expressions matched (case-insensitively) against the full URL. |
| `query_contains` | string | no | Default substring a matching URL must contain (per-domain values override this). |
| `label` | string | no | Title for the standalone fallback admonition (table-only links). Default `Links`. |

\* A rule must provide at least one of `domains` or `patterns`.

!!! tip
    Enable [`content.footnote.tooltips`](../reference/theme.md) in your theme `features`
    so the notes appear as hover tooltips as well as in the footer.
