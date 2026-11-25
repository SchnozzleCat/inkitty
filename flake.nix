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
    ];
  in {
    devShells.${system}.default = pkgs.mkShell {
      packages = [pkgs.dotnetCorePackages.sdk_11_0] ++ runtimeLibs;

      LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibs;
      DOTNET_ROOT = "${pkgs.dotnetCorePackages.sdk_11_0}/share/dotnet";
      DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    };
  };
}
