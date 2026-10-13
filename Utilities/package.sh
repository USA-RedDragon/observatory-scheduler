#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version=$(sed -n 's/^\[assembly: AssemblyFileVersion("\([^"]*\)")\]/\1/p' NINA.Plugin.TargetScheduler/Properties/AssemblyInfo.cs)
out=NINA.Plugin.TargetScheduler/bin/Release/net8.0-windows7.0
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
dir="$stage/Observatory Scheduler"
mkdir -p "$dir"
while read -r f; do
  [ -n "$f" ] && cp "$out/$f" "$dir/"
done < Utilities/package-files.txt
cp LICENSE.txt "$dir/"
mkdir -p dist
name=observatory-scheduler-$version
if [ -n "${NINA_VERSION:-}" ] && [ "$NINA_VERSION" != 3.1.2.9001 ]; then name=$name-nina-$NINA_VERSION; fi
rm -f "dist/$name.zip"
(cd "$stage" && zip -qrX - "Observatory Scheduler") > "dist/$name.zip"
echo "dist/$name.zip"
