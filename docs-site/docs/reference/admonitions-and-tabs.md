---
title: Admonitions & tabs
---

# Admonitions & content tabs

Admonitions (call-outs) and content tabs are two of the most-used Material features. This page
shows every variant **rendered live** next to its Markdown, so you can copy the exact syntax
you need. Both are always available — admonitions and tabs work out of the box.

## Admonition types

Write an admonition with `!!!`, a **type** keyword, and an optional `"Title"`. The type sets
the color and icon. Each type below is rendered as it will appear on your site.

!!! note

    `!!! note` — general information the reader should notice.

!!! abstract

    `!!! abstract` (aliases: `summary`, `tldr`) — a short summary.

!!! info

    `!!! info` (alias: `todo`) — supplementary information.

!!! tip

    `!!! tip` (aliases: `hint`, `important`) — a helpful suggestion.

!!! success

    `!!! success` (aliases: `check`, `done`) — something that worked.

!!! question

    `!!! question` (aliases: `help`, `faq`) — an open question or FAQ entry.

!!! warning

    `!!! warning` (aliases: `caution`, `attention`) — proceed carefully.

!!! failure

    `!!! failure` (aliases: `fail`, `missing`) — something that did not work.

!!! danger

    `!!! danger` (alias: `error`) — a critical or dangerous note.

!!! bug

    `!!! bug` — a known bug or defect.

!!! example

    `!!! example` — a worked example.

!!! quote

    `!!! quote` (alias: `cite`) — a quotation or citation.

## Titles

The first argument is a custom title. Pass an empty string to render an admonition with **no**
title bar.

=== "Result"

    !!! tip "Pin your toolchain"

        Commit a `global.json` so every contributor builds with the same SDK.

    !!! warning ""

        A titleless admonition — just the colored body.

=== "Markdown"

    ```markdown
    !!! tip "Pin your toolchain"

        Commit a `global.json` so every contributor builds with the same SDK.

    !!! warning ""

        A titleless admonition — just the colored body.
    ```

## Collapsible admonitions

Use `???` for a collapsed block and `???+` for one that starts expanded. Everything else is the
same as `!!!`.

=== "Result"

    ??? note "Click to expand"

        Hidden until the reader opens it — great for optional detail or long output.

    ???+ info "Expanded by default"

        Starts open, but the reader can collapse it.

=== "Markdown"

    ```markdown
    ??? note "Click to expand"

        Hidden until the reader opens it.

    ???+ info "Expanded by default"

        Starts open, but the reader can collapse it.
    ```

## Content tabs

Group alternatives with `=== "Label"`. Tabs can contain any Markdown — prose, code, lists, even
admonitions or nested tabs.

=== "Result"

    === "Debian / Ubuntu"

        ```bash
        sudo apt install ./netdocs_amd64.deb
        ```

    === "Windows"

        ```powershell
        .\netdocs.exe --help
        ```

    === "Docker"

        ```bash
        docker run --rm -v "$PWD:/site" ghcr.io/xtremeownage/netdocs:latest build
        ```

=== "Markdown"

    ````markdown
    === "Debian / Ubuntu"

        ```bash
        sudo apt install ./netdocs_amd64.deb
        ```

    === "Windows"

        ```powershell
        .\netdocs.exe --help
        ```

    === "Docker"

        ```bash
        docker run --rm ... build
        ```
    ````

!!! tip "Link tabs across the page"
    Enable the [`content.tabs.link`](theme.md) feature so that selecting a tab label (e.g.
    “Windows”) switches **every** tab group with that label on the page at once — handy for
    OS-specific instructions repeated throughout a document.

### Admonitions inside tabs

Because tab content is ordinary Markdown, you can nest a call-out to draw attention within a
single tab:

=== "Result"

    === "Stable"

        !!! success "Recommended"
            Pin a released version tag for reproducible builds.

    === "Edge"

        !!! warning "Moves fast"
            `latest` tracks `main` and may change without notice.

=== "Markdown"

    ```markdown
    === "Stable"

        !!! success "Recommended"
            Pin a released version tag for reproducible builds.

    === "Edge"

        !!! warning "Moves fast"
            `latest` tracks `main` and may change without notice.
    ```

## Enabling the extensions

Admonitions, collapsible details, and tabs are backed by these `markdownExtensions` entries
(all included in the [recommended configuration](../plugins/index.md#recommended-configuration)):

```json
"markdownExtensions": [
  { "name": "admonition" },
  { "name": "pymdownx.details" },
  { "name": "pymdownx.superfences" },
  { "name": "pymdownx.tabbed", "options": { "alternate_style": true } }
]
```

See the full [Markdown extensions reference](markdown-extensions.md) for everything else in the
pipeline.
