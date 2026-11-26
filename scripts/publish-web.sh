#!/usr/bin/env bash
# Publishes the browser build of Inkitty to artifacts/web (static files; serve with any web server).
#
# The browser build needs the wasm-tools workload. If `dotnet workload list` shows it, the local SDK
# is used; otherwise (e.g. the read-only Nix SDK) the build runs in the official SDK container with
# podman or docker. Sources are copied into the container, so it writes nothing but artifacts/web.
#
# Usage: scripts/publish-web.sh [--serve [port]]
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$repo/artifacts/web"
project="src/Inkitty.Browser/Inkitty.Browser.csproj"
image="inkitty-web-build:11.0"

publish_args=(-c Release -p:SuppressTrimAnalysisWarnings=true)

if dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
    dotnet publish "$repo/$project" "${publish_args[@]}" -o "$out"
else
    engine="$(command -v podman || command -v docker || true)"
    if [[ -z "$engine" ]]; then
        echo "error: the wasm-tools workload isn't installed and neither podman nor docker is available." >&2
        echo "       Install it with 'dotnet workload install wasm-tools', or install podman/docker." >&2
        exit 1
    fi

    if ! "$engine" image exists "$image" 2>/dev/null && ! "$engine" image inspect "$image" >/dev/null 2>&1; then
        echo "Building $image (one-off: .NET SDK + wasm-tools workload)..."
        "$engine" build -t "$image" -f "$repo/scripts/web-build.Containerfile" "$repo/scripts"
    fi

    rm -rf "$out"
    mkdir -p "$out"
    "$engine" run --rm \
        -v "$repo:/repo:ro" \
        -v "$out:/out" \
        -v inkitty-nuget:/root/.nuget/packages \
        --tmpfs /work:exec,size=4g \
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
        "$image" sh -ec "
            cd /repo
            tar --exclude='./artifacts' --exclude='./.git' --exclude='./.worktrees' --exclude='*/bin' --exclude='*/obj' --exclude='./TestResults' -cf - . | tar -C /work -xf -
            cd /work
            dotnet publish $project ${publish_args[*]} -o /work/publish
            cp -r /work/publish/wwwroot/. /out/
            chown -R $(id -u):$(id -g) /out
        "
fi

# Self-contained static site: GitHub Pages must not run Jekyll over _framework/.
if [[ -d "$out/wwwroot" ]]; then
    tmp="$(mktemp -d)"
    mv "$out/wwwroot"/* "$tmp"/ && rm -rf "$out" && mv "$tmp" "$out"
fi
touch "$out/.nojekyll"
echo "Published to $out"

if [[ "${1:-}" == "--serve" ]]; then
    port="${2:-8080}"
    echo "Serving on http://localhost:$port"
    cd "$out" && exec python3 -m http.server "$port"
fi
