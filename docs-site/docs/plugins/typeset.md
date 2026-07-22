---
title: typeset
---

# typeset

Smart typography for prose. Enables Markdig's [SmartyPants](https://daringfireball.net/projects/smartypants/)
so common ASCII punctuation is replaced with its typographically-correct equivalent as the
page is parsed:

| You type | You get |
|---|---|
| `"quotes"` | curly “quotes” |
| `'apostrophes'` | curly ‘apostrophes’ |
| `--` | en dash (–) |
| `---` | em dash (—) |
| `...` | ellipsis (…) |

Code spans and fenced code blocks are left **untouched**, so command lines and code samples
keep their literal quotes and dashes.

## Usage

Enable the plugin — there is nothing else to configure. Every page's prose is typeset
automatically.

```markdown
"It just works" --- no configuration, no per-page opt-in...
```

renders as:

> “It just works” — no configuration, no per-page opt-in…

## Options

This plugin has no options.

```json
{ "name": "typeset" }
```

## Attribution

Provided by [Markdig](https://github.com/xoofx/markdig)'s SmartyPants extension, modeled on
John Gruber's original [SmartyPants](https://daringfireball.net/projects/smartypants/).
See [Attributions](../about/attributions.md).
