---
title: Grids
nav_title: Grids
icon: grid
---

# Grids

Grids arrange content into responsive columns so a page can be split into a tidy set of
sections — landing pages, feature overviews, "next steps" cards, and side-by-side comparisons.
They are built in to the theme; no plugin or configuration is required. Columns wrap
automatically to fit the viewport, and collapse to a single column on narrow screens.

There are two flavours: **card grids** (each cell is a bordered card) and **plain grids**
(each cell is an arbitrary block of content).

## Card grids

Wrap a Markdown **bullet list** in a `<div class="grid cards" markdown>` block. Each list item
becomes a card. Leave a blank line after the opening tag so the list inside is parsed as
Markdown.

```markdown
<div class="grid cards" markdown>

- :material-rocket-launch: **Get started**

    ---

    Install Netdocs and build your first site in a couple of minutes.

    [:octicons-arrow-right-24: Quickstart](getting-started/quickstart.md)

- :material-puzzle: **Plugins**

    ---

    Extend the build with search, tags, blog, RSS, social cards, and more.

    [:octicons-arrow-right-24: Browse plugins](plugins/index.md)

- :material-cog: **Configure**

    ---

    Tune the theme, navigation, and markdown extensions to taste.

    [:octicons-arrow-right-24: Configuration](reference/configuration.md)

- :material-cloud-upload: **Deploy**

    ---

    Publish to GitHub Pages, S3, Docker, or any static host.

    [:octicons-arrow-right-24: Publishing](setup/publishing.md)

</div>
```

<div class="grid cards" markdown>

- :material-rocket-launch: **Get started**

    ---

    Install Netdocs and build your first site in a couple of minutes.

- :material-puzzle: **Plugins**

    ---

    Extend the build with search, tags, blog, RSS, social cards, and more.

- :material-cog: **Configure**

    ---

    Tune the theme, navigation, and markdown extensions to taste.

- :material-cloud-upload: **Deploy**

    ---

    Publish to GitHub Pages, S3, Docker, or any static host.

</div>

The `---` inside a card renders as a divider between the card's heading and its body.

## Plain grids

For arbitrary content (not just cards), use `<div class="grid" markdown>` and make each cell its
own block. Any element with the `card` class inside a plain grid still gets the card treatment,
so you can mix bordered and borderless cells.

```markdown
<div class="grid" markdown>

=== "Tab A"

    Content for the first column.

=== "Tab B"

    Content for the second column.

</div>
```

## How it works

The theme lays a `.grid` out with CSS `grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr))`,
so cells are at least `16rem` wide and wrap to as many columns as fit. Card cells
(`.grid.cards > ul > li` and `.grid > .card`) get a border, padding, and a subtle hover shadow.
Because the layout is pure CSS, grids work with instant navigation and print correctly.

!!! tip "Keep cards short"

    Card grids read best with a bold heading, one or two sentences, and a single call-to-action
    link. Long prose is better as normal paragraphs.
