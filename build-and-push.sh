#!/bin/bash

set -euo pipefail

# Default behavior: do not push unless --push is passed
PUSH=false
POSITIONAL=()

# Parse optional flags (--push / -p), leave the rest as positional (registry [version])
for arg in "$@"; do
  case "$arg" in
    --push|-p)
      PUSH=true
      ;;
    --help|-h)
      echo "Usage: $0 [--push] <registry> [version]"
      echo "Example: $0 --push registry.formaniuk.com v1.0.0"
      echo "If version is not specified, 'latest' will be used."
      echo "You can also pass 'gitversion' as version to compute it locally via GitVersion CLI."
      exit 0
      ;;
    *)
      POSITIONAL+=("$arg")
      ;;
  esac
done

# Restore positional parameters
set -- "${POSITIONAL[@]}"

# Check if registry argument is provided
if [ -z "${1-}" ]; then
  echo "Usage: $0 [--push] <registry> [version]"
  echo "Example: $0 --push registry.formaniuk.com v1.0.0"
  echo "If version is not specified, 'latest' will be used."
  echo "You can also pass 'gitversion' as version to compute it locally via GitVersion CLI."
  exit 1
fi

REGISTRY=$1
VERSION=${2:-latest}

# If user requested GitVersion-derived version, try to compute it locally
if [ "$VERSION" = "gitversion" ]; then
  if command -v gitversion >/dev/null 2>&1; then
    # Try the simple showvariable form first
    SEMVER=$(dotnet gitversion -showvariable SemVer 2>/dev/null || dotnet gitversion /showvariable SemVer 2>/dev/null || true)
    OUT=""
    if [ -z "$SEMVER" ]; then
      # Try JSON output and parse SemVer
      OUT=$(dotnet gitversion -output json 2>/dev/null || dotnet gitversion /output json 2>/dev/null || true)
      SEMVER=$(printf '%s' "$OUT" | sed -n 's/.*"SemVer"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
    fi

    if [ -z "$SEMVER" ]; then
      echo "ERROR: gitversion ran but could not determine SemVer. Output (if any):"
      printf '%s
' "$OUT"
      echo "Install GitVersion CLI with: dotnet tool install -g GitVersion.Tool"
      exit 1
    fi

    VERSION="$SEMVER"
    echo "Using GitVersion computed version: $VERSION"
  else
    echo "ERROR: gitversion CLI not found. Install with: dotnet tool install -g GitVersion.Tool"
    echo "Or pass an explicit semver version as the second argument."
    exit 1
  fi
fi

# Validate VERSION: allow 'latest', 8-char short SHA, or semver MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
if [ "$VERSION" != "latest" ]; then
  if [[ "$VERSION" =~ ^[0-9a-f]{8}$ ]]; then
    echo "VERSION is a short commit SHA: $VERSION"
  else
    if [[ ! "$VERSION" =~ ^([0-9]+)\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
      echo "ERROR: VERSION '$VERSION' is not valid semver (expected MAJOR.MINOR.PATCH, optional -PRERELEASE and +BUILD)"
      exit 1
    fi
    echo "VERSION is valid semver: $VERSION"
  fi
else
  echo "Using special version 'latest'"
fi

# Build Proxy image
echo "Building Fwda.Proxy image (VERSION=$VERSION)..."
docker build --build-arg VERSION="$VERSION" -f Fwda.Proxy/Dockerfile -t "$REGISTRY/fwda-proxy:$VERSION" .

# Build Watcher image
echo "Building Fwda.Watcher image (VERSION=$VERSION)..."
docker build --build-arg VERSION="$VERSION" -f Fwda.Watcher/Dockerfile -t "$REGISTRY/fwda-watcher:$VERSION" .

if [ "$PUSH" = true ]; then
  ## Push Proxy image
  echo "Pushing Fwda.Proxy image..."
  docker push "$REGISTRY/fwda-proxy:$VERSION"

  ## Push Watcher image
  echo "Pushing Fwda.Watcher image..."
  docker push "$REGISTRY/fwda-watcher:$VERSION"
else
  echo "--push not provided; skipping docker push steps. Use --push to enable pushing images to the registry."
fi

echo "Build completed. PUSH=${PUSH}"
