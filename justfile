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
publish-web:
    bash scripts/publish-web.sh

# Publish the browser version and serve it on http://localhost:<port>.
serve port="8080": publish-web
    dotnet tool restore
    dotnet serve --directory artifacts/web --port {{port}}

# Publish the browser version and deploy it to Cloudflare Pages (credentials from pass, or the environment).
[script("bash")]
deploy project="inkitty" branch="main":
    set -euo pipefail
    # Already-set variables win (e.g. CI); otherwise read the first line of each pass entry.
    CLOUDFLARE_API_TOKEN="${CLOUDFLARE_API_TOKEN:-$(pass show cloudflare/pages-api-token | head -n1)}"
    CLOUDFLARE_ACCOUNT_ID="${CLOUDFLARE_ACCOUNT_ID:-$(pass show cloudflare/account-id | head -n1)}"
    if [[ -z "$CLOUDFLARE_API_TOKEN" || -z "$CLOUDFLARE_ACCOUNT_ID" ]]; then
        echo "error: empty Cloudflare credentials (pass cloudflare/pages-api-token, cloudflare/account-id)" >&2
        exit 1
    fi
    export CLOUDFLARE_API_TOKEN CLOUDFLARE_ACCOUNT_ID
    just publish-web
    just wrangler pages deploy artifacts/web --project-name {{project}} --branch {{branch}} \
        --commit-hash "$(git rev-parse HEAD)" --commit-message "$(git log -1 --format=%s)"

# Run the pinned wrangler (tools/deploy), installed from its lockfile with install scripts disabled.
# Arguments are passed through as-is ("$@"), so values with spaces or quotes stay intact.
[positional-arguments]
wrangler *args:
    cd tools/deploy && npm ci --ignore-scripts --no-audit --no-fund --loglevel=error
    WRANGLER_SEND_METRICS=false node tools/deploy/node_modules/wrangler/bin/wrangler.js "$@"
