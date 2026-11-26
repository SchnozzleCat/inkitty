# Inkitty

An IDE for [ink](https://github.com/inkle/ink), inkle's narrative scripting language, built with
[Avalonia](https://avaloniaui.net/) and modern C# as an alternative to
[Inky](https://github.com/inkle/inky) and runs on **Linux, Windows and macOS**, and in the browser at
**<https://schnozzlecat.github.io/inkitty/>**.

> [!CAUTION]
> This is alpha software that was created to fit my own needs, so your mileage may vary.
> Please create issues for any problems you run into.
> I don't have access to a machine running macOS, and while Avalonia should technically compile just fine, I would appreciate testing on this platform as I am sure there will be UI-related issues.

## Features

- **Play as you write**, replaying your choices after every edit so you stay where you are in the story.
- **Syntax highlighting** to easily differentiate knots, diverts, choices and gathers, labels, tags, glue, conditionals and sequences.
- **Outline** of the current file, showing what it contains at a quick glance.
- **Story map** drawing the story as a flow chart, as well as where you currently are, and how often knots have been visited.
- **Variables** showing the story's global variables and their current state.
- **Colour themes** so you don't burn your eyes.
- **Error highlighting** so any syntax mistakes are quickly spotted.
- **Issue browser** showing errors, warnings, and TODOs across the project.
- **Jump to definition** for knots, stitches, labels, variables, constants, lists, functions and parameters, across files.
- **Multi-file projects** with `INCLUDE`s. Writing an include for a file that doesn't exist creates it.
- **File watching** with unsaved edit detection.
- **Docking** so you can customise the experience.

## Downloads

Builds for Linux, Windows and macOS (x64 and Arm each) are attached to each
[release](https://github.com/SchnozzleCat/inkitty/releases). Each is a single executable that
needs no .NET install.

On Linux, make the download executable first: `chmod +x inkitty-linux-x64`.

The macOS app isn't signed with an Apple developer certificate, so macOS blocks it the first time.
Open the `.dmg` and drag Inkitty to Applications. Then either run
`xattr -dr com.apple.quarantine /Applications/Inkitty.app`, or try to open it once and
then allow it under *System Settings > Privacy & Security*.

## Building and running

Requires the .NET 11 SDK (see `global.json`). The ink compiler is built from source from a git
submodule.

```sh
git clone --recurse-submodules <repo-url> inkitty   # or: git submodule update --init
cd inkitty
dotnet run --project src/Inkitty.Desktop -- samples/multi-file/main.ink
```

On Linux, Inkitty uses X11 (XWayland on Wayland desktops) by default. When there is no X server
it switches to Avalonia's native Wayland backend, which can be overridden with
`INKITTY_WAYLAND=1` or `INKITTY_WAYLAND=0`.

### With Nix

A flake is provided to run the app on Linux (x86_64 or aarch64):

```sh
nix run github:SchnozzleCat/inkitty                          # start Inkitty
nix run github:SchnozzleCat/inkitty -- path/to/main.ink      # open a story
```

The flake also provides a devshell for `nix develop` (or direnv) with the SDK and the
native libraries Avalonia needs. The flake fetches ink separately, pinned to the same commit as the
`external/ink` submodule, and NuGet packages are locked in `deps.json`. After changing package
versions, regenerate it with `nix build .#inkitty.fetch-deps && ./result deps.json`.

### In the browser

Inkitty also runs in a browser tab using AOT-compiled WebAssembly. The latest release is served at
<https://schnozzlecat.github.io/inkitty/>.
Projects are kept in the browser's storage; _File > Open…_ lists them and imports a
project folder, `.ink` files or a `.zip`, and _File > Export Project as .zip…_ downloads a copy.

> [!WARNING]
> Clearing the site's data deletes the projects. Back up your work regularly.

```sh
just publish-web    # writes the site to artifacts/web (scripts/publish-web.sh)
just serve          # ...and serves it on http://localhost:8080 (dotnet-serve; `just serve 9000` for another port)
```

The browser build needs the `wasm-tools` workload (`dotnet workload install wasm-tools`). If it isn't
installed, the script builds in the .NET SDK container with podman or
docker. Release builds compile everything to WebAssembly ahead of time, so the first publish takes
a few minutes.

### Tests

```sh
dotnet test
```

All tests use [TUnit](https://tunit.dev/):

- `tests/Inkitty.Core.Tests` covers the compiler service, replay, symbols, include scanning, the
  outline, the story graph, file watching, settings and the browser's project storage.
- `tests/Inkitty.App.Tests` drives the real UI with Avalonia's headless platform. It covers
  play-as-you-write, navigation, the outline, story map and variables, highlighting, themes,
  menus, file watching, auto-save and docking.

### Publishing

```sh
just publish linux-x64   # or win-x64, osx-arm64, osx-x64; writes artifacts/<rid>
```

For a macOS `.app` bundle, put the published output in `Inkitty.app/Contents/MacOS/` and
`src/Inkitty.Desktop/Info.plist` in `Inkitty.app/Contents/`. The plist registers `.ink` files so Finder
can open them with Inkitty.

## Keyboard shortcuts

| Action                        | Linux / Windows       | macOS                 |
| ----------------------------- | --------------------- | --------------------- |
| New / Open project            | Ctrl+N / Ctrl+O       | Cmd+N / Cmd+O         |
| Save / Save all               | Ctrl+S / Ctrl+Shift+S | Cmd+S / Cmd+Shift+S   |
| Close tab                     | Ctrl+W                | Cmd+W                 |
| Go to definition              | F12, Ctrl+click       | F12, Cmd+click        |
| Back / Forward                | Alt+← / Alt+→         | Ctrl+- / Ctrl+Shift+- |
| Next / previous issue         | F8 / Shift+F8         | F8 / Shift+F8         |
| Restart story                 | Ctrl+R                | Cmd+R                 |
| Show the story's current line | Ctrl+J                | Cmd+J                 |
| Show the map                  | Ctrl+Shift+M          | Cmd+Shift+M           |
| Settings                      | Ctrl+,                | Cmd+,                 |

## Where things are stored

Settings (`settings.json`) and the dock layout (`layout.json`) are stored in:

- Linux: `$XDG_CONFIG_HOME/Inkitty`, or `~/.config/Inkitty` if that isn't set
- Windows: `%APPDATA%\Inkitty`
- macOS: `~/Library/Application Support/Inkitty`

Set `INKITTY_CONFIG_DIR` to use a different directory. In the browser, settings and projects live in
the site's IndexedDB storage.

## How it works

Rather than writing your files to a temp directory and running the `inklecate` tool, Inkitty compiles **in-process** with ink's C# compiler.

- Unsaved editor buffers are served to the compiler from memory, so no temp files are needed.
- The compiler's parsed hierarchy gives exact symbol positions for jump-to-definition. These
  are available even when compilation fails.
- The compiled story runs directly in the player.

```
src/Inkitty.Core      project model, compilation, issues, symbols, playback, file watching, settings (no UI)
src/Inkitty.App       Avalonia UI library: docking shell, editor control, tools, view models
src/Inkitty.Desktop   desktop executable (Linux, Windows, macOS)
src/Inkitty.Browser   browser executable (WebAssembly) and its web page
tests/                TUnit tests for Core and App
samples/              example ink projects (multi-file; The Intercept, the browser's sample)
```

## Licence

Inkitty is released under the [MIT License](LICENSE). ink and _The Intercept_ (`samples/the-intercept`)
are also MIT, © inkle Ltd; their notices are in `external/ink/LICENSE.txt` and
`samples/the-intercept/README.md`.
