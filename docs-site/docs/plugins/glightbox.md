---
title: glightbox
---

# glightbox

Adds an image lightbox powered by
[GLightbox](https://github.com/biati-digital/glightbox). Content images are wrapped in
lightbox links and open in a zoomable overlay with touch navigation.

## Usage

Enable the plugin — no options required:

```json
{ "name": "glightbox" }
```

The plugin injects the GLightbox CSS/JS and an initializer that wraps every
`.md-content img` (that isn't already linked) so clicking an image opens it in the
lightbox.

## Attribution

Behavior is modeled on [mkdocs-glightbox](https://github.com/blueswen/mkdocs-glightbox) + [GLightbox](https://github.com/biati-digital/glightbox) (MIT). See [Attributions](../about/attributions.md).
