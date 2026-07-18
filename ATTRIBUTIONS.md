# Attributions & derivative works

Netdocs is an independent .NET implementation, but it deliberately mirrors the
behavior, configuration, and output of a number of excellent open-source projects so
that existing [Material for MkDocs](https://github.com/squidfunk/mkdocs-material)
sites build with little to no change. This file credits those upstream works.

Netdocs is **not** affiliated with or endorsed by any of the projects below. All
trademarks and copyrights belong to their respective owners.

## Theme & framework

| Upstream | Author | License | What Netdocs derives |
|---|---|---|---|
| [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) | Martin Donath (@squidfunk) | MIT | Theme templates, vendored CSS/JS, markup class names, feature flags, and overall UX. |
| [MkDocs](https://github.com/mkdocs/mkdocs) | MkDocs contributors | BSD-2-Clause | Configuration model (nav, plugins, markdown extensions) and site layout conventions. |

## Plugins

Each plugin reimplements the behavior of an upstream MkDocs plugin or PyMdown extension.

| Netdocs plugin | Modeled on | Author | License |
|---|---|---|---|
| search | mkdocs-material search + [lunr](https://lunrjs.com/) | @squidfunk / Oliver Nightingale | MIT |
| social | mkdocs-material social cards | @squidfunk | MIT |
| blog | mkdocs-material blog | @squidfunk | MIT |
| tags | mkdocs-material tags | @squidfunk | MIT |
| meta | mkdocs-material meta | @squidfunk | MIT |
| snippets | [pymdownx.snippets](https://facelessuser.github.io/pymdown-extensions/extensions/snippets/) | @facelessuser | MIT |
| b64 | [pymdownx.b64](https://facelessuser.github.io/pymdown-extensions/extensions/b64/) | @facelessuser | MIT |
| abbreviations | Python-Markdown `abbr` / PyMdown Extensions | Python-Markdown | BSD-3-Clause |
| macros | [mkdocs-macros-plugin](https://github.com/fralau/mkdocs-macros-plugin) | @fralau | MIT |
| git-revision-date | [mkdocs-git-revision-date-localized-plugin](https://github.com/timvink/mkdocs-git-revision-date-localized-plugin) | @timvink | MIT |
| redirects | [mkdocs-redirects](https://github.com/mkdocs/mkdocs-redirects) | MkDocs contributors | MIT |
| glightbox | [mkdocs-glightbox](https://github.com/blueswen/mkdocs-glightbox) + [GLightbox](https://github.com/biati-digital/glightbox) | Blueswen / biati digital | MIT |
| rss | [mkdocs-rss-plugin](https://github.com/Guts/mkdocs-rss-plugin) | @Guts | MIT |
| table-reader | [mkdocs-table-reader-plugin](https://github.com/timvink/mkdocs-table-reader-plugin) | @timvink | MIT |
| file-filter | mkdocs-file-filter | community | MIT |

## Bundled front-end libraries

| Library | Author | License | Use |
|---|---|---|---|
| [highlight.js](https://github.com/highlightjs/highlight.js) | highlight.js contributors | BSD-3-Clause | Code block syntax highlighting. |
| [Mermaid](https://github.com/mermaid-js/mermaid) | Knut Sveidqvist & contributors | MIT | Diagrams from fenced ```` ```mermaid ```` blocks. |
| [Twemoji](https://github.com/jdecked/twemoji) | Twitter / jdecked | Code: MIT · Graphics: CC-BY 4.0 | Emoji shortcode rendering. |
| [Roboto / Roboto Mono](https://fonts.google.com/specimen/Roboto) | Christian Robertson / Google | Apache-2.0 | Default body and code fonts. |

## License

Netdocs itself is distributed under the [MIT License](LICENSE). Reusing any of the
works above remains subject to their own licenses, which are linked from each project.
