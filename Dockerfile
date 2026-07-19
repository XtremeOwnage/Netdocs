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
COPY --from=build /app /opt/netdocs
RUN ln -s /opt/netdocs/netdocs /usr/local/bin/netdocs
EXPOSE 8000
ENTRYPOINT ["netdocs"]
CMD ["build"]
