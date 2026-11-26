# Build image for the browser version of Inkitty: the .NET SDK plus the wasm-tools workload
# (Emscripten), which the Nix SDK can't install. Used by scripts/publish-web.sh.
FROM mcr.microsoft.com/dotnet/sdk:11.0
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/* \
 && dotnet workload install wasm-tools
