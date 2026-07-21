---
title: Docker
---

# Docker

Netdocs ships a `Dockerfile` and a workflow that builds and publishes a container image
to the GitHub Container Registry (GHCR). The image bundles the `netdocs` CLI so you can
build sites without installing .NET.

## Using the image

Mount your site directory at `/site` (the image's working directory) and run any CLI
command:

```bash
# Build
docker run --rm -v "$PWD:/site" ghcr.io/xtremeownage/netdocs:latest build

# Serve (publish the port)
docker run --rm -p 8000:8000 -v "$PWD:/site" \
  ghcr.io/xtremeownage/netdocs:latest serve --port 8000
```

The container's entrypoint is `netdocs`, so arguments after the image name are passed
straight through.

## The Dockerfile

A multi-stage build compiles a self-contained, single-file executable and copies the
publish folder (the binary plus the `theme/` templates and assets it needs at runtime)
into a small runtime image:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Netdocs.Cli -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-preview
WORKDIR /site
COPY --from=build /app /opt/netdocs
RUN ln -s /opt/netdocs/netdocs /usr/local/bin/netdocs
ENTRYPOINT ["netdocs"]
CMD ["build"]
```

## Building locally

```bash
docker build -t netdocs:local .
docker run --rm -v "$PWD/docs-site:/site" netdocs:local build
```

## Publishing (CI)

`.github/workflows/docker.yml` builds the image and pushes it to GHCR on pushes to `main`
and on tags, tagging images with the git ref and `latest`.
