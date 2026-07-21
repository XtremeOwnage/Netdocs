# Security Policy

## Supported versions

Netdocs is a hobby project. Security fixes, when they happen, land on `main` and the
latest release. There is no long-term support commitment — see the
["Why should you use this project?"](README.md#why-should-you-use-this-project) note.

| Version | Supported |
| --- | --- |
| `main` / latest release | ✅ best effort |
| older releases | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Instead, use GitHub's
[private vulnerability reporting](https://github.com/XtremeOwnage/Netdocs/security/advisories/new)
for this repository, or reach the maintainer privately via the
[Discord](https://static.xtremeownage.com/discord).

Please include:

- A description of the vulnerability and its impact.
- Steps to reproduce (a proof of concept if possible).
- Affected version(s) or commit.

You can expect a best-effort acknowledgement. Because this is a spare-time project,
response times vary. Coordinated disclosure is appreciated — give a reasonable window for
a fix before publishing details.

## Scope

Netdocs is a static site generator: it reads local Markdown/config and writes HTML. The
most relevant risks are around build-time inputs (untrusted `appsettings.json`, external
plugin DLLs, and Markdown/macro processing). Treat external plugin assemblies as
trusted code — only load `plugins/*.dll` you built or trust.
