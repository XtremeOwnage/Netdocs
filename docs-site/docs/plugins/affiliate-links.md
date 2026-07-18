---
title: affiliate-links
---

# affiliate-links

Automatically annotates outbound **affiliate links** (for example eBay Partner Network
`ebay.us` links or tagged Amazon links) so that:

- hovering the link shows the program's disclosure as a **tooltip**, and
- the disclosure is rendered **once at the bottom of the page** (the footer),

without you having to hand-add a footnote or disclosure snippet to every post. It is a
first-party Netdocs plugin — there is no equivalent MkDocs plugin — and is fully opt-in and
data-driven.

## How it works

The plugin runs as a Markdown preprocessor (order `30`, after
[snippets](snippets.md), [table-reader](table-reader.md) and [macros](macros.md), so links
those plugins generate are also covered). For every configured *program* it scans the page
for links whose host matches one of the program's domains and appends a footnote reference
carrying the disclosure text. Because the tooltip and the footer disclosure both come from
the same footnote, enabling the Material
[`content.footnote.tooltips`](../reference/theme.md) feature gives you the hover behavior for
free.

```json
{
  "name": "affiliate-links",
  "options": {
    "programs": [
      {
        "name": "ebay",
        "domains": [ "ebay.us" ],
        "disclosure": "This is an eBay Partner Network affiliate link. It costs you nothing extra, and purchases made through it help support this site."
      }
    ]
  }
}
```

A link such as `[HBA card](https://ebay.us/abc123)` becomes, in the rendered page, a link
with a small footnote marker; hovering it reveals the disclosure, and the same text appears
in the page's footnote list.

## Matching by query parameter

Some programs only treat a link as an affiliate link when it carries a specific query
parameter — for example a raw `amazon.com` URL is only an affiliate link when it has a
`tag=` parameter, while `amzn.to` short links always are. A domain entry can therefore be
either a plain string or an object with its own `query_contains` marker:

```json
{
  "name": "amazon",
  "domains": [
    "amzn.to",
    { "domain": "amazon.com", "query_contains": "tag=" }
  ],
  "disclosure": "This is an Amazon affiliate link. As an Amazon Associate I earn from qualifying purchases at no additional cost to you; it helps support this site."
}
```

A program-level `query_contains` may also be set; it applies to every plain-string domain
that doesn't override it.

## What is left untouched

To keep output valid, the plugin does **not** inject a footnote reference when:

- the link **already carries a footnote** (e.g. a hand-authored `[^ebay]`), so it coexists
  with existing content during migration;
- the link is inside a **fenced code block**;
- the link is inside a **pipe-table cell** (Markdig can't reliably parse footnote references
  there); or
- the link is **glued directly to an adjacent link** (`[a](x)[b](y)`), where a wedged
  footnote would render ambiguously.

In the table-cell and adjacent-link cases the footer disclosure is still guaranteed: the
program emits a standalone `!!! info "Affiliate links"` admonition at the bottom of the page
instead of a footnote.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `programs` | array | — | The affiliate programs to detect (see below). |

Each **program** object:

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Program id; used to build the footnote label (`affiliate-<name>`). |
| `disclosure` | string | yes | Markdown shown as the tooltip and footer disclosure. |
| `domains` | array | yes | Hosts that identify the program's links. Each entry is a domain string or `{ "domain": "...", "query_contains": "..." }`. Subdomains match automatically. |
| `query_contains` | string | no | Default substring a matching URL must contain (per-domain values override this). |

!!! tip
    Enable [`content.footnote.tooltips`](../reference/theme.md) in your theme `features`
    so the disclosures appear as hover tooltips as well as in the footer.
