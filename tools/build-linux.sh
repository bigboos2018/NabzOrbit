#!/usr/bin/env bash
# Build the Windows GUI, not a Linux DNS changer. Dependencies: mono-mcs,
# WinForms/Drawing assemblies, gcc (preprocessing only), MinGW windres.
set -euo pipefail
cd "$(dirname "$0")/.."
output="${1:-release}"
mkdir -p "$output"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
"${WINDRES:-i686-w64-mingw32-windres}" --preprocessor="${CC:-gcc}" \
    --preprocessor-arg=-E --preprocessor-arg=-xc -I assets \
    -i assets/app.rc -O res -o "$tmp/app.res"
refs=()
if [[ -n "${NETFX_REFS:-}" ]]; then
    refs+=(-noconfig -nostdlib "-r:$NETFX_REFS/mscorlib.dll")
    for ref in System System.Core System.Drawing System.Windows.Forms; do
        refs+=("-r:$NETFX_REFS/$ref.dll")
    done
else
    refs+=(-r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll)
fi
mcs -langversion:5 -codepage:utf8 -optimize+ -platform:anycpu -target:winexe \
    "-out:$output/NabzOrbit.exe" "-win32res:$tmp/app.res" \
    -resource:assets/catalog.tsv,Nabz.Catalog -resource:assets/Vazirmatn-Regular.ttf,Orbit.Regular -resource:assets/Vazirmatn-Bold.ttf,Orbit.Bold -resource:assets/orbit-cover.jpg,Orbit.Cover -resource:assets/OFL.txt,Orbit.FontLicense "${refs[@]}" src/*.cs
printf 'Built %s/NabzOrbit.exe\n' "$output"
