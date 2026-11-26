{
  description = "Inkitty development shell";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = {nixpkgs, ...}: let
    system = "x86_64-linux";
    pkgs = nixpkgs.legacyPackages.${system};

    # Native libraries dlopen'd at runtime by Avalonia / SkiaSharp / HarfBuzzSharp.
    runtimeLibs = with pkgs; [
      fontconfig
      freetype
      libGL
      libxkbcommon
      wayland
      libx11
      libice
      libsm
      libxi
      libxcursor
      libxrandr
      libxext
      icu
      openssl
      zlib
      stdenv.cc.cc.lib
      just
    ];
  in {
    devShells.${system}.default = pkgs.mkShell {
      packages = [pkgs.dotnetCorePackages.sdk_11_0 pkgs.nodejs] ++ runtimeLibs; # node: wrangler for `just deploy`

      LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibs;
      DOTNET_ROOT = "${pkgs.dotnetCorePackages.sdk_11_0}/share/dotnet";
      DOTNET_CLI_TELEMETRY_OPTOUT = "1";

      # npm hardening for anything run in this shell (tools/deploy/.npmrc repeats it for that package):
      # never run dependency install scripts, only install versions public for at least a week, and
      # never fetch dependencies straight from git.
      NPM_CONFIG_IGNORE_SCRIPTS = "true";
      NPM_CONFIG_MIN_RELEASE_AGE = "7";
      NPM_CONFIG_ALLOW_GIT = "none";
      NPM_CONFIG_UPDATE_NOTIFIER = "false";
      NPM_CONFIG_FUND = "false";
    };
  };
}
