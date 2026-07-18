---
title: arithmatex (math)
---

# arithmatex (math)

Typesets LaTeX math with [MathJax](https://www.mathjax.org/), mirroring the
[`pymdownx.arithmatex`][arithmatex] extension. Inline and block math are parsed into
dedicated elements — code spans and fenced code blocks are never touched — so `$`
signs in your prose and snippets stay literal.

## Usage

Inline math uses single dollars (or `\(…\)`):

```markdown
The mass–energy equivalence is $E = mc^2$.
```

Block math uses double dollars (or `\[…\]`):

```markdown
$$
\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
$$
```

Rendered live on this page (the docs site enables `arithmatex`): the mass–energy
equivalence is $E = mc^2$, and the Gaussian integral evaluates to

$$
\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}.
$$

## Enabling

Add the plugin to the `plugins` array:

```json
{ "name": "arithmatex" }
```

Math is rendered client-side: Markdig converts `$…$`/`$$…$$` into
`<span class="math">\(…\)</span>` and `<div class="math">\[…\]</div>`, and MathJax v3
typesets those delimiters (and re-typesets on `navigation.instant` page swaps).

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `mathjax_url` | string | MathJax v3 CDN (`tex-mml-chtml`) | URL of the MathJax bundle. Point this at a vendored copy for offline/air-gapped builds. |

```json
{ "name": "arithmatex", "options": { "mathjax_url": "/assets/vendor/mathjax/tex-mml-chtml.js" } }
```

!!! note "Offline builds"
    The default loads MathJax from jsDelivr. For sites that must build without
    internet access, download the MathJax `es5` bundle into your `docs/` assets and set
    `mathjax_url` to its site-relative path.

## Attribution

Behavior is modeled on [`pymdownx.arithmatex`][arithmatex] from Facelessuser's PyMdown
Extensions (MIT), rendered with [MathJax](https://github.com/mathjax/MathJax) (Apache-2.0).
See [Attributions](../about/attributions.md).

[arithmatex]: https://facelessuser.github.io/pymdown-extensions/extensions/arithmatex/
