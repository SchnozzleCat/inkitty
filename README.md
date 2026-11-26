# Inkitty

An IDE for [ink](https://github.com/inkle/ink), inkle's narrative scripting language, built with
[Avalonia](https://avaloniaui.net/) and modern C#. It's a prototype alternative to
[Inky](https://github.com/inkle/inky) and runs on **Linux, Windows and macOS**.

## Features

- **Play as you write.** The player re-runs the story after every edit and replays your choices,
  so you stay where you were. Choices are matched by text, so inserting or reordering choices
  doesn't lose your place. Restart and step back are one click away.
- **Syntax highlighting** with a TextMate grammar for ink (`tools/ink-grammar/generate.py`) that every bundled colour theme understands: knots, diverts, choices and gathers, labels, tags, glue, conditionals and sequences, and logic as real expressions.
- **Colour themes:** 18 built-in themes (Light+/Dark+, Dracula, Monokai, Solarized, One Dark/Light,
  Visual Studio, Abyss, High Contrast, ...) that restyle the whole window, not just the editor,
  or "System" to follow the OS light/dark setting. The editor font is the bundled JetBrains Mono.
- **Error highlighting.** Live recompiles as you type show squiggles, gutter markers and
  tooltips. Unresolved diverts are underlined at the exact token, not just the line.
- **Issue browser** listing errors, warnings, TODOs and runtime errors, with filters. Click an
  issue, or press F8 / Shift+F8, to jump to it.
- **Jump to definition** for knots, stitches, labels, variables, constants, lists, functions and
  parameters, across files. Use Ctrl/Cmd+click (Alt/Option+click optional) or F12. Go back and
  forward through navigation history.
- **Multi-file projects** with no project file. Open the main `.ink` and Inkitty follows its
  `INCLUDE`s. Typing `INCLUDE new.ink` creates the file.
- **File watching.** Files changed on disk are reloaded. If you have unsaved edits, an info bar
  lets you reload or keep yours.
- **Docking.** The default layout is three panes (Files | Editor + Issues | Player). Every pane
  can be moved, tabbed, floated or pinned, and the layout is remembered.
- **Settings** for auto-save (off / on focus lost / after a delay), colour theme, editor font, word wrap,
  recompile delay and replay time limit.

## Building and running

Requires the .NET 11 SDK (see `global.json`). The ink compiler is built from source from a git
submodule.

```sh
git clone --recurse-submodules <repo-url> inkitty   # or: git submodule update --init
cd inkitty
dotnet run --project src/Inkitty.Desktop -- samples/multi-file/main.ink
```

On NixOS, `nix develop` (or direnv) provides the SDK and the native libraries Avalonia needs.

On Linux, Inkitty uses X11 (XWayland on Wayland desktops) by default. When there is no X server
it switches to Avalonia's native Wayland backend. You can force that choice with
`INKITTY_WAYLAND=1` or `INKITTY_WAYLAND=0`.

### In the browser

Inkitty also runs in a browser tab (WebAssembly), served as static files. Projects are kept in the
browser's storage (IndexedDB); *File > Open…* lists them and imports a project folder, `.ink`
files or a `.zip`, and *File > Export Project as .zip…* downloads a copy. Clearing the site's data
deletes the projects, so export regularly.

```sh
just publish-web    # writes the site to artifacts/web (scripts/publish-web.sh)
just serve          # ...and serves it on http://localhost:8080 (dotnet-serve; `just serve 9000` for another port)
```

The browser build needs the `wasm-tools` workload (`dotnet workload install wasm-tools`). If it isn't
installed (the Nix SDK is read-only), the script builds in the .NET SDK container with podman or
docker. `.github/workflows/web.yml` publishes `main` to GitHub Pages. Design notes and limitations:
[docs/web.md](docs/web.md).

### Tests

```sh
dotnet test
```

All tests use [TUnit](https://tunit.dev/):

- `tests/Inkitty.Core.Tests` covers the compiler service, replay, symbols, include scanning,
  file watching and settings.
- `tests/Inkitty.App.Tests` drives the real UI with Avalonia's headless platform. It covers
  play-as-you-write, navigation, file watching, auto-save and docking.

### Publishing

```sh
dotnet publish src/Inkitty.Desktop -c Release -r linux-x64 --self-contained   # or win-x64, osx-arm64, osx-x64
```

For a macOS `.app` bundle, put the published output in `Inkitty.app/Contents/MacOS/` and
`src/Inkitty.Desktop/Info.plist` in `Inkitty.app/Contents/`. The plist registers `.ink` files so Finder
can open them with Inkitty.

## Keyboard shortcuts

| Action | Linux / Windows | macOS |
|---|---|---|
| New / Open project | Ctrl+N / Ctrl+O | Cmd+N / Cmd+O |
| Save / Save all | Ctrl+S / Ctrl+Shift+S | Cmd+S / Cmd+Shift+S |
| Close tab | Ctrl+W | Cmd+W |
| Go to definition | F12, Ctrl+click | F12, Cmd+click |
| Back / Forward | Alt+← / Alt+→ | Ctrl+- / Ctrl+Shift+- |
| Next / previous issue | F8 / Shift+F8 | F8 / Shift+F8 |
| Restart story | Ctrl+R | Cmd+R |
| Settings | Ctrl+, | Cmd+, |

## Where things are stored

Settings (`settings.json`) and the dock layout (`layout.json`) are stored in:

- Linux: `$XDG_CONFIG_HOME/Inkitty`, or `~/.config/Inkitty` if that isn't set
- Windows: `%APPDATA%\Inkitty`
- macOS: `~/Library/Application Support/Inkitty`

Set `INKITTY_CONFIG_DIR` to use a different directory.

## How it works

Inky writes your files to a temp folder and runs the `inklecate` command-line compiler on them.
Inkitty instead compiles **in-process** with ink's C# compiler (`external/ink`, pinned to v1.2.1):

- Unsaved editor buffers are served to the compiler from memory, so no temp files are needed.
- The compiler's parsed hierarchy gives exact symbol positions for jump-to-definition. These
  are available even when compilation fails.
- The compiled story runs directly in the player. It runs in time slices with time and output
  limits, so an endless loop can't freeze the IDE.

```
src/Inkitty.Core      project model, compilation, issues, symbols, playback, file watching, settings (no UI)
src/Inkitty.App       Avalonia UI library: docking shell, editor control, tools, view models
src/Inkitty.Desktop   desktop executable (Linux, Windows, macOS)
src/Inkitty.Browser   browser executable (WebAssembly) and its web page
tests/                TUnit tests for Core and App
samples/              example ink projects
```

## Licence

Inkitty's code is yours to license. ink is MIT, © inkle Ltd.
