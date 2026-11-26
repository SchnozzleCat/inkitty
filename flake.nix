{
  description = "Inkitty, an IDE for inkle's ink";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    # ink's compiler, at the same commit as the external/ink submodule (keep the two in sync).
    # Fetched as an input because flakes leave submodules out, and Lix has no `self.submodules`.
    ink = {
      url = "github:inkle/ink/35c63e52f1d36060930dc7ed3cfba38ea224b528";
      flake = false;
    };
  };

  outputs = {
    self,
    nixpkgs,
    ink,
  }: let
    systems = ["x86_64-linux" "aarch64-linux"];
    forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});

    # Native libraries dlopen'd at runtime by Avalonia / SkiaSharp / HarfBuzzSharp.
    runtimeLibs = pkgs:
      with pkgs; [
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
      ];
  in {
    packages = forAllSystems (pkgs: {
      default = self.packages.${pkgs.stdenv.hostPlatform.system}.inkitty;

      # The desktop app. `nix run` starts it; pass an .ink file to open it.
      inkitty = pkgs.buildDotnetModule {
        pname = "inkitty";
        version = "0-unstable-${builtins.substring 0 8 (self.lastModifiedDate or "19700101")}";

        src = self;

        # The submodule is empty in the flake's source; put the pinned ink input in its place.
        postPatch = ''
          rm -rf external/ink
          cp -r ${ink} external/ink
          chmod -R u+w external/ink
        '';

        projectFile = "src/Inkitty.Desktop/Inkitty.Desktop.csproj";
        # NuGet packages, locked with hashes. After changing package versions, regenerate with
        # `nix build .#inkitty.fetch-deps && ./result deps.json`.
        nugetDeps = ./deps.json;

        dotnet-sdk = pkgs.dotnetCorePackages.sdk_11_0;
        dotnet-runtime = pkgs.dotnetCorePackages.runtime_11_0;

        executables = ["Inkitty"];
        # Avalonia's build tasks phone home; there's no network in the sandbox anyway.
        AVALONIA_TELEMETRY_OPTOUT = "1";
        runtimeDeps = runtimeLibs pkgs;

        # A lower-case name on PATH, like other Linux apps.
        postFixup = ''
          ln -s $out/bin/Inkitty $out/bin/inkitty
        '';

        meta = {
          description = "An IDE for inkle's ink narrative scripting language";
          homepage = "https://github.com/SchnozzleCat/inkitty";
          license = nixpkgs.lib.licenses.mit;
          mainProgram = "inkitty";
          platforms = systems;
        };
      };
    });

    apps = forAllSystems (pkgs: {
      default = {
        type = "app";
        program = nixpkgs.lib.getExe self.packages.${pkgs.stdenv.hostPlatform.system}.inkitty;
        meta.description = "Run the Inkitty desktop app";
      };
    });

    devShells = forAllSystems (pkgs: {
      default = pkgs.mkShell {
        packages = [pkgs.dotnetCorePackages.sdk_11_0 pkgs.just] ++ runtimeLibs pkgs;

        LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath (runtimeLibs pkgs);
        DOTNET_ROOT = "${pkgs.dotnetCorePackages.sdk_11_0}/share/dotnet";
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
      };
    });
  };
}
