<#
.SYNOPSIS
  Builds MidiMesh and makes the installer, portable zip and update packages with Velopack.

.DESCRIPTION
  The release workflow runs this, and so can you, to get the same files locally.
  For each runtime it publishes a self-contained build and packs it on its own channel
  (win-x64, win-arm64), so each installed copy only ever updates to its own CPU type.

  Output, per runtime, in -OutputDir:
    MidiMesh-win-x64-Setup.exe        installer
    MidiMesh-win-x64-Portable.zip     portable copy (unzip and run, still updates itself)
    MidiMesh-<version>-win-x64-full.nupkg, -delta.nupkg, releases.win-x64.json, RELEASES-win-x64
                                      the update feed files the app reads

  To make delta updates, put the previous release's files in -OutputDir first
  (the workflow does this with "vpk download github").

.EXAMPLE
  ./build/pack.ps1 -Version 0.1.0
  ./build/pack.ps1 -Version 0.1.1 -Runtimes win-x64 -OutputDir C:\temp\feed
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string[]] $Runtimes = @('win-x64', 'win-arm64'),
    [string] $OutputDir = (Join-Path $PSScriptRoot '..\artifacts\releases'),
    [string] $PublishDir = (Join-Path $PSScriptRoot '..\artifacts\publish'),
    # The GitHub repository the app checks for updates. Empty keeps the one in MidiMesh.csproj.
    [string] $RepositoryUrl = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\MidiMesh\MidiMesh.csproj'
$icon = Join-Path $root 'src\MidiMesh\Assets\MidiMesh.ico'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not like 1.2.3 or 1.2.3-beta.1."
}

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'dotnet tool restore failed.' }

    foreach ($rid in $Runtimes) {
        $platform = @{ 'win-x64' = 'x64'; 'win-arm64' = 'ARM64' }[$rid]
        if (-not $platform) { throw "Unknown runtime '$rid'. Use win-x64 or win-arm64." }

        $out = Join-Path $PublishDir $rid
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }

        Write-Host "== Publishing $Version for $rid"
        $extra = @()
        if ($RepositoryUrl) { $extra += "-p:RepositoryUrl=$RepositoryUrl" }
        dotnet publish $project -c Release -r $rid -p:Platform=$platform --self-contained true `
            -p:Version=$Version @extra -o $out
        if ($LASTEXITCODE) { throw "dotnet publish failed for $rid." }
        if (-not (Test-Path (Join-Path $out 'MidiMesh.pri'))) {
            throw "MidiMesh.pri is missing from the $rid build; the app would crash on start."
        }

        Write-Host "== Packing $Version for $rid"
        dotnet vpk pack --packId MidiMesh --packVersion $Version --packDir $out `
            --mainExe MidiMesh.exe --packTitle MidiMesh --packAuthors Clinteastman `
            --icon $icon --channel $rid --runtime $rid --outputDir $OutputDir
        if ($LASTEXITCODE) { throw "vpk pack failed for $rid." }
    }
}
finally {
    Pop-Location
}

Write-Host "== Done. Files in $OutputDir"
