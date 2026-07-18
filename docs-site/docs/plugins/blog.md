---
title: blog
---

# blog

Turns Markdown posts under `{blog_dir}/posts/` into a blog: rewritten post URLs, a
paginated index, category and archive listing pages, post metadata, and a listing
sidebar with **Archive** (years) and **Categories**.

## Layout

```text
docs/
└─ blog/
   ├─ index.md          # becomes page 1 of the paginated list
   └─ posts/
      ├─ 2025-05-01-hello.md
      └─ Project-X/
         └─ update.md   # folder name derives the category
```

## Post front matter

```yaml
---
date: 2025-05-01
slug: my-custom-slug        # optional — overrides the title-derived slug
categories: [Technology]
tags: [dotnet, ssg]
authors: [jane]
---
# My post title

Intro paragraph shown as the excerpt.

<!-- more -->

Full body…
```

- `date` — used for the post URL and ordering (falls back to file mtime).
- `slug` — optional explicit URL slug. When omitted, the slug is derived from the post
  **title** (front-matter `title:` or the first `H1`), *not* the file name — matching
  MkDocs/Material. So a post titled "Hacking KVM with IP Control" stored as
  `2025-02-24-KVM-Esphome.md` publishes at `blog/2025/hacking-kvm-with-ip-control/`.
- `categories` — explicit categories; otherwise derived from the post's folder.
- The excerpt is the content up to `<!-- more -->` (or the first paragraph), with any
  leading `H1` stripped and length capped so it stays a concise teaser.

The post URL is `{blog_dir}{date:post_url_date_format}/{slug}/`.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `blog_dir` | string | `"blog/"` | Blog root within `docs`. |
| `post_url_date_format` | string | `"yyyy"` | Date segment in post URLs. |
| `pagination_per_page` | int | `20` | Posts per index page. |
| `categories` | bool | `true` | Generate category listing pages. |
| `archive` | bool | `true` | Generate per-year archive pages. |

```json
{
  "name": "blog",
  "options": {
    "blog_dir": "blog/",
    "post_url_date_format": "yyyy",
    "pagination_per_page": 20,
    "categories": true,
    "archive": true
  }
}
```

## Generated pages

- `blog/` — paginated index (page 1); `blog/page/2/`, `blog/page/3/`, …
- `blog/category/<slug>/` — one per category.
- `blog/archive/<year>/` — one per year.
- `blog/author/<id>/` — one per author who has posts.

Each post page also gets a metadata header (date · read-time · categories) and, on
listing pages, an Archive/Categories/Authors sidebar.

## Authors

Provide `{blog_dir}/.authors.yml` to attach author name/role/avatar to posts:

```yaml
authors:
  jane:
    name: Jane Doe
    description: Maintainer
    avatar: /assets/authors/jane.png
```

Reference authors from a post's front matter (multiple are supported):

```yaml
authors: [jane, john]
```

Each post shows its authors (avatar, name, role) with the name linking to that
author's page at `blog/author/<id>/`, which lists every post they wrote. When a blog
defines exactly one author, posts without an explicit `authors` entry fall back to it.
Author pages and the Authors sidebar entry are generated automatically.

## Attribution

Behavior is modeled on the [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) blog plugin (MIT). See [Attributions](../about/attributions.md).
