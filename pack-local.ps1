<#
.SYNOPSIS
  Packs Hpgl.Rendering to a local NuGet feed so a consuming project can be tested
  against a real package before anything is published.

.DESCRIPTION
  Once a consumer switches from a ProjectReference to a PackageReference, the inner
  loop gets slower: edit -> pack -> bump -> restore. This keeps that loop local.

  Wire a consuming repo up once, by adding a NuGet.config beside its solution:

      <?xml version="1.0" encoding="utf-8"?>
      <configuration>
        <packageSources>
          <add key="local" value="C:\Users\Tony\source\local-nuget" />
        </packageSources>
      </configuration>

  Then reference the version this script prints. Bump -Suffix on each iteration:
  NuGet caches by exact version, so re-packing the same version number will silently
  keep serving the old bits.

  To clear a cached iteration:  dotnet nuget locals global-packages --clear

.PARAMETER Feed
  Local folder to publish into. Created if missing.

.PARAMETER Suffix
  Prerelease suffix, appended to the MinVer-derived version. Bump it every pack.
#>
[CmdletBinding()]
param(
    [string]$Feed   = "$HOME\source\local-nuget",
    [string]$Suffix = "local"
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $Feed | Out-Null

$out = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet pack (Join-Path $PSScriptRoot 'src\Hpgl.Rendering\Hpgl.Rendering.csproj') `
    -c Release -o $out "/p:MinVerPreRelease=$Suffix"
if ($LASTEXITCODE -ne 0) { throw "pack failed" }

Get-ChildItem $out -Filter *.nupkg | ForEach-Object {
    Copy-Item $_.FullName -Destination $Feed -Force
    Write-Host "-> $($_.Name) published to $Feed" -ForegroundColor Green
}
