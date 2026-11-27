# Inkitty tasks. Run `just` to list them.

default:
    @just --list

# Build and run the desktop app (optionally with a main ink file).
run *args:
    dotnet run --project src/Inkitty.Desktop -- {{args}}

# Run all tests.
test:
    dotnet test --solution Inkitty.slnx

# Publish the self-contained desktop app to artifacts/<rid> (e.g. linux-x64, linux-arm64, win-x64, osx-arm64).
publish rid="linux-x64":
    dotnet publish src/Inkitty.Desktop -c Release -r {{rid}} --self-contained -o artifacts/{{rid}}

# Publish the browser version to artifacts/web.
publish-web *args:
    bash scripts/publish-web.sh {{args}}

# Render assets/icon/cat.svg into the app's icon files (Windows, macOS, window, browser).
icons:
    bash scripts/make-icons.sh

# Publish the browser version and serve it on http://localhost:<port>.
serve port="8080": publish-web
    dotnet tool restore
    dotnet serve --directory artifacts/web --port {{port}}
