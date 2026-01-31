FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

ARG VERSION=latest

# Copy project file and restore dependencies
COPY ["Fwda.Watcher/Fwda.Watcher.csproj", "Fwda.Watcher/"]
COPY ["Fwda.Shared/Fwda.Shared.csproj", "Fwda.Shared/"]
RUN dotnet restore "Fwda.Watcher/Fwda.Watcher.csproj" -r linux-musl-x64

# Copy source code and build
COPY ["Fwda.Watcher/", "Fwda.Watcher/"]
COPY ["Fwda.Shared/", "Fwda.Shared/"]
RUN dotnet publish "Fwda.Watcher/Fwda.Watcher.csproj" -c Release -r linux-musl-x64 \
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

ARG VERSION=latest

# Install ICU libraries required by .NET 10
RUN apk add --no-cache icu-libs

# Copy published app
COPY --from=build /app/publish .

LABEL version="$VERSION"

ENTRYPOINT ["./Fwda.Watcher"]
