# syntax=docker/dockerfile:1

# Build a self-contained, single-file netdocs CLI, then copy it into a small
# runtime-deps image. The Material theme is emitted next to the executable
# (theme/templates, theme/assets), so the whole publish folder is carried over.
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Netdocs.Cli -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-preview
WORKDIR /site
# Ship a font so the social-cards plugin can render text. The minimal runtime-deps
# image has no fonts, which otherwise makes SixLabors.Fonts fail with
# "Cannot use the default value type instance to create a font".
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core fontconfig \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app /opt/netdocs
RUN ln -s /opt/netdocs/netdocs /usr/local/bin/netdocs
EXPOSE 8000
ENTRYPOINT ["netdocs"]
CMD ["build"]
