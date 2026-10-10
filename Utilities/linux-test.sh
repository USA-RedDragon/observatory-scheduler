#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: DOTNET_WINDESKTOP_ROOT=<dotnet 8 root that has shared/Microsoft.WindowsDesktop.App> $0 [dotnet test args, e.g. --filter FullyQualifiedName~Scoring]" >&2
  exit 2
}

repo=$(cd "$(dirname "$0")/.." && pwd)
src=${DOTNET_WINDESKTOP_ROOT:-${DOTNET_ROOT:-}}
if [ -z "$src" ] || [ ! -d "$src/shared/Microsoft.WindowsDesktop.App" ] || [ ! -x "$src/dotnet" ]; then
  usage
fi
src=$(cd "$src" && pwd)

cache=${TS_LINUX_TEST_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/ts-linux-test}
overlay=$cache/dotnet
tool=$cache/stripr2r
proj=NINA.Plugin.TargetScheduler.Test

if [ ! -f "$overlay/.ready" ] || [ "$(cat "$overlay/.ready")" != "$src" ]; then
  rm -rf "$overlay" "$tool"
  mkdir -p "$overlay/shared" "$tool"
  cp "$src/dotnet" "$overlay/"
  for d in host sdk packs sdk-manifests templates; do
    if [ -e "$src/$d" ]; then ln -s "$src/$d" "$overlay/$d"; fi
  done
  for d in "$src"/shared/*; do
    if [ "$(basename "$d")" != Microsoft.WindowsDesktop.App ]; then ln -s "$d" "$overlay/shared/$(basename "$d")"; fi
  done
  cp -r "$src/shared/Microsoft.WindowsDesktop.App" "$overlay/shared/"

  cat > "$tool/stripr2r.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Mono.Cecil" Version="0.11.3" />
    <PackageReference Include="Microsoft.Win32.SystemEvents" Version="8.0.0" />
    <PackageReference Include="System.CodeDom" Version="8.0.0" />
    <PackageReference Include="System.Configuration.ConfigurationManager" Version="8.0.0" />
    <PackageReference Include="System.Diagnostics.EventLog" Version="8.0.0" />
    <PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.0" />
    <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
    <PackageReference Include="System.Security.Cryptography.Pkcs" Version="8.0.0" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
    <PackageReference Include="System.Security.Cryptography.Xml" Version="8.0.0" />
  </ItemGroup>
</Project>
EOF
  cat > "$tool/Program.cs" <<'EOF'
using System;
using System.IO;
using Mono.Cecil;

foreach (var dir in args) {
    foreach (var f in Directory.GetFiles(dir, "*.dll")) {
        try {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(dir);
            using var ms = new MemoryStream(File.ReadAllBytes(f));
            using var ad = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = resolver });
            var m = ad.MainModule;
            if (m.Architecture == TargetArchitecture.I386 && !m.Attributes.HasFlag(ModuleAttributes.ILLibrary)) {
                continue;
            }
            m.Attributes &= ~(ModuleAttributes.ILLibrary | ModuleAttributes.Required32Bit);
            m.Attributes |= ModuleAttributes.ILOnly;
            m.Architecture = TargetArchitecture.I386;
            ad.Write(f + ".tmp");
            File.Move(f + ".tmp", f, true);
        } catch (Exception e) {
            Console.Error.WriteLine($"skipped {Path.GetFileName(f)}: {e.Message}");
        }
    }
}
EOF
  DOTNET_ROOT=$src "$src/dotnet" build "$tool/stripr2r.csproj" -c Release > "$cache/stripr2r.log" 2>&1 || { cat "$cache/stripr2r.log" >&2; exit 1; }
  nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
  for f in "$overlay"/shared/Microsoft.WindowsDesktop.App/*/*.dll; do
    b=$(basename "$f" .dll)
    l=$(echo "$b" | tr '[:upper:]' '[:lower:]')
    for c in "$nuget/$l"/8.*/lib/net8.0/"$b".dll; do
      if [ -f "$c" ]; then cp "$c" "$f"; fi
    done
  done
  DOTNET_ROOT=$src "$src/dotnet" run --no-build --project "$tool/stripr2r.csproj" -c Release -- "$overlay"/shared/Microsoft.WindowsDesktop.App/*/
  echo "$src" > "$overlay/.ready"
fi

log=$cache/build.log
if ! DOTNET_ROOT=$src "$src/dotnet" build "$repo/$proj/$proj.csproj" -p:EnableWindowsTargeting=true -p:GenerateSupportedOSPlatformAttribute=false > "$log" 2>&1; then
  grep -E ": error " "$log" | sort -u >&2 || true
  echo "build failed, see $log" >&2
  exit 1
fi

PATH=$overlay:$PATH DOTNET_ROOT=$overlay TZ=${TS_TEST_TZ:-America/New_York} \
  "$overlay/dotnet" test "$repo/$proj/$proj.csproj" --no-build "$@"
