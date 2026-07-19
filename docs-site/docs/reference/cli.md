---
title: CLI reference
---

# CLI reference

The `netdocs` executable exposes two commands: `build` and `serve`.

```text
netdocs - static site generator

Usage:
  netdocs build [options]     Build the site to the output directory
  netdocs serve [options]     Serve with live reload
```

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`siteDir`). |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |
| `netdocs --help` | Print usage. |

If no command is given, `build` is assumed.

## Options

| Option | Alias | Description |
|---|---|---|
| `--config <path>` | `-f` | Path to `appsettings.json` (default `./appsettings.json`). |
| `--port <port>` | `-p` | Dev server port for `serve` (default `8000`). |
| `--clean` | | Remove existing output before building. |
| `--strict` | | Fail on plugin/template errors. |
| `--prod` | `--production` | Production build (enables prod-only plugins such as social cards). |
| `--verbose` | `-v` | Verbose (Trace) logging. |

!!! note
    `build` always cleans by default. `serve` does an incremental in-place rebuild and
    only cleans when you pass `--clean`.

## Examples

Build using an explicit config file:

```pwsh
netdocs build --config ./appsettings.json
```

Production build with verbose logging:

```pwsh
netdocs build --prod --verbose
```

Serve on a specific port:

```pwsh
netdocs serve --port 8080
```

## Environment variables

Configuration can be overridden with environment variables prefixed `NETDOCS_`
(standard `Microsoft.Extensions.Configuration` binding). Production builds also set
`MKDOCS_PROD_BUILD=true` so prod-only behaviour (like social-card generation) activates.

## Logging

Logging is the standard .NET `Logging` section of `appsettings.json`. Set per-category
levels, for example:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Build": "Debug",
      "Netdocs": "Trace"
    }
  }
}
```

The `--verbose` flag forces `Trace` globally.
