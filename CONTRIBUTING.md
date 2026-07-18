# Contributing to Netdocs

Thanks for taking an interest! Netdocs is a nights-and-weekends project (see the
["Why should you use this project?"](README.md#why-should-you-use-this-project) note in the
README), so please keep expectations calibrated: PRs and issues are welcome, but reviews
happen when life allows.

## Ground rules

- Be kind. See the [Code of Conduct](CODE_OF_CONDUCT.md).
- Keep changes focused. One logical change per pull request.
- Add or update tests for behavior changes.
- Update docs (`docs-site/docs/**`) when you change user-facing behavior.

## Getting set up

You need the .NET SDK pinned in [`global.json`](global.json).

```pwsh
git clone https://github.com/XtremeOwnage/Netdocs
cd Netdocs
dotnet restore Netdocs.slnx
dotnet build Netdocs.slnx -c Debug
dotnet test Netdocs.slnx
```

Build and serve the dogfood docs site while you work:

```pwsh
dotnet run --project src/Netdocs.Cli -- serve --config docs-site/appsettings.json
```

## Before you open a PR

Run the same checks CI runs, plus formatting:

```pwsh
dotnet format Netdocs.slnx --verify-no-changes
dotnet build Netdocs.slnx -c Release
dotnet test Netdocs.slnx -c Release
```

`dotnet format` enforces the rules in [`.editorconfig`](.editorconfig). Run
`dotnet format Netdocs.slnx` (without `--verify-no-changes`) to auto-fix.

## Where things live

| Path | What |
| --- | --- |
| `src/Netdocs.Abstractions` | Plugin contracts + page/site models. |
| `src/Netdocs.Core` | Config, discovery, Markdig pipeline, Scriban templating, build engine. |
| `src/Netdocs.Plugins` | Built-in plugins. |
| `src/Netdocs.Theme.Material` | Scriban templates + theme assets. |
| `src/Netdocs.Cli` | `netdocs build|serve|watch`. |
| `tests/Netdocs.Core.Tests` | xUnit tests (one file per plugin/area). |
| `docs-site/` | The self-hosted documentation site. |

## Writing a plugin

See [Events & callbacks](docs-site/docs/development/events-and-callbacks.md) and the
[build lifecycle](docs-site/docs/development/lifecycle.md). New built-in plugins get their
own test file named after the plugin (e.g. `RssPluginTests.cs`).

## Commit messages

Short imperative subject line ("Add X", "Fix Y"). Explain the *why* in the body when it
isn't obvious.

## Questions

Open a [discussion or issue](https://github.com/XtremeOwnage/Netdocs/issues), or say hi in
the [Discord](https://static.xtremeownage.com/discord) (tag me).
