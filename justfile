# Inkitty tasks. Run `just` to list them.

default:
    @just --list

# Build and run the desktop app (optionally with a main ink file).
run *args:
    dotnet run --project src/Inkitty.Desktop -- {{args}}

# Run all tests.
test:
    dotnet test --solution Inkitty.slnx

# Publish the browser version to artifacts/web.
publish-web:
    bash scripts/publish-web.sh

# Publish the browser version and serve it on http://localhost:<port>.
serve port="8080": publish-web
    dotnet tool restore
    dotnet serve --directory artifacts/web --port {{port}}
