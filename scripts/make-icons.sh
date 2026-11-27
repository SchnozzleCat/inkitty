#!/usr/bin/env bash
# Renders assets/icon/cat.svg (unchanged, see the README for its licence) into the app's icon files.
# Run `just icons` inside the dev shell (`nix develop`), which provides rsvg-convert, magick and png2icns.
#
# Writes:
#   src/Inkitty.Desktop/Assets/Icons/inkitty.ico   Windows executable icon (ApplicationIcon)
#   src/Inkitty.Desktop/Assets/Icons/inkitty.icns  macOS app bundle icon (Info.plist CFBundleIconFile)
#   src/Inkitty.App/Assets/Icons/inkitty.png       window icon (title bar, taskbar, Linux docks)
#   src/Inkitty.Browser/wwwroot/favicon.ico        browser tab icon
#   src/Inkitty.Browser/wwwroot/cat.svg            browser loading screen logo (the SVG itself)
set -euo pipefail
cd "$(dirname "$0")/.."

svg=assets/icon/cat.svg
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

png() { echo "$tmp/$1.png"; }
for size in 16 20 24 32 40 48 64 128 256 512 1024; do
  rsvg-convert --width "$size" --height "$size" --keep-aspect-ratio "$svg" --output "$(png "$size")"
done

mkdir -p src/Inkitty.Desktop/Assets/Icons src/Inkitty.App/Assets/Icons
magick $(for s in 16 20 24 32 40 48 64 128 256; do png "$s"; done) src/Inkitty.Desktop/Assets/Icons/inkitty.ico
png2icns src/Inkitty.Desktop/Assets/Icons/inkitty.icns $(for s in 16 32 48 128 256 512 1024; do png "$s"; done) >/dev/null
cp "$(png 256)" src/Inkitty.App/Assets/Icons/inkitty.png
magick $(for s in 16 24 32 48 64; do png "$s"; done) src/Inkitty.Browser/wwwroot/favicon.ico
cp "$svg" src/Inkitty.Browser/wwwroot/cat.svg

ls -l src/Inkitty.Desktop/Assets/Icons/inkitty.{ico,icns} src/Inkitty.App/Assets/Icons/inkitty.png src/Inkitty.Browser/wwwroot/{favicon.ico,cat.svg}
