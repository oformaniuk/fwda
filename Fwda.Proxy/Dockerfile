FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Use a NuGet-valid version by default (restore evaluates Version properties).
ARG VERSION=0.0.0

# Copy project file and restore dependencies
COPY ["Fwda.Proxy/Fwda.Proxy.csproj", "Fwda.Proxy/"]
COPY ["Fwda.Shared/Fwda.Shared.csproj", "Fwda.Shared/"]
RUN dotnet restore "Fwda.Proxy/Fwda.Proxy.csproj" -r linux-musl-x64 \
    -p:Version=$VERSION \
    -p:FileVersion=$VERSION \
    -p:InformationalVersion=$VERSION

# Copy source code and build
COPY ["Fwda.Proxy/", "Fwda.Proxy/"]
COPY ["Fwda.Shared/", "Fwda.Shared/"]
RUN dotnet publish "Fwda.Proxy/Fwda.Proxy.csproj" -c Debug -r linux-musl-x64 \
    --self-contained true \
    -p:PublishTrimmed=true \
    -p:TrimMode=partial \
    -p:Version=$VERSION \
    -p:FileVersion=$VERSION \
    -p:InformationalVersion=$VERSION \
    -o /app/publish

# Runtime stage - use Alpine base (no runtime needed for self-contained)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
WORKDIR /app

ARG VERSION=0.0.0

# Install curl for health checks and ICU libraries required by .NET 10
RUN apk add --no-cache curl icu-libs

# Copy published app
COPY --from=build /app/publish .

# Create config directory
RUN mkdir -p /config
RUN mkdir -p /keys

# Run as non-root user for security
RUN addgroup -g 1000 authuser && \
    adduser -u 1000 -G authuser -s /bin/sh -D authuser && \
    chown -R authuser:authuser /app /config /keys && \
    chmod 0755 /config

USER authuser

# Expose port
EXPOSE 5005

## Health check
HEALTHCHECK --interval=5m --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:5005/health || exit 1

LABEL fwda.proxy="true"
LABEL version="$VERSION"

ENTRYPOINT ["./Fwda.Proxy"]
