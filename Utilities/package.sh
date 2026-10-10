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
rm -f "dist/observatory-scheduler-$version.zip"
(cd "$stage" && zip -qrX - "Observatory Scheduler") > "dist/observatory-scheduler-$version.zip"
echo "dist/observatory-scheduler-$version.zip"
