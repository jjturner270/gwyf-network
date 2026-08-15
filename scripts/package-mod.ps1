<#
.SYNOPSIS
  Builds a reproducible Release package of the GWYF Community Network mod in
  BepInEx layout and produces the release ZIP.

.DESCRIPTION
  Requires GWYF_REFS (or -GwyfRefs) pointing at the game's interop references
  (BepInEx/interop on BepInEx 6 IL2CPP installs) so the game-bound plugin layer
  compiles. Without it the plugin DLL would contain no BepInEx entry point.

  Output layout:
    dist/
      GWYFCommunityNetwork-<version>.zip
      BepInEx/plugins/GWYFCommunityNetwork/GWYFCommunityNetwork.dll
      BepInEx/plugins/GWYFCommunityNetwork/BouncyCastle.Cryptography.dll
      BepInEx/plugins/GWYFCommunityNetwork/README.txt

  The mod DLL itself is built deterministically (Deterministic +
  ContinuousIntegrationBuild) — same refs + same sources → same bytes. The ZIP
  container is not normalized (Compress-Archive keeps entry timestamps/order),
  so successive ZIP hashes may differ even though build and layout are stable.
#>
[CmdletBinding()]
param(
    [string]$GwyfRefs = $env:GWYF_REFS,
    [string]$OutDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")).Path "dist"),
    [string]$Version = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GwyfRefs) -or -not (Test-Path -LiteralPath $GwyfRefs)) {
    throw "GWYF_REFS must point at the game's interop references (BepInEx/interop). Set GWYF_REFS or pass -GwyfRefs."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$csproj = Join-Path $repoRoot "mod\GWYFCommunityNetwork.csproj"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$proj = Get-Content -LiteralPath $csproj
    $Version = $proj.Project.PropertyGroup[0].Version
}
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "0.0.0" }

$pkgName = "GWYFCommunityNetwork"
$stage = Join-Path $OutDir "BepInEx\plugins\$pkgName"
$pluginDir = Join-Path $stage "GWYFCommunityNetwork.dll"
$zipPath = Join-Path $OutDir "$pkgName-$Version.zip"

Write-Host "Building $pkgName $Version (Release, refs: $GwyfRefs)"
& dotnet build $csproj -c $Configuration -p:GWYF_REFS=$GwyfRefs -p:Version=$Version -v:minimal
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$bin = Join-Path $repoRoot "mod\bin\$Configuration\net6.0"
$dll = Join-Path $bin "$pkgName.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "missing build output: $dll" }

Write-Host "Staging BepInEx layout"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $dll -Destination $pluginDir -Force

$bc = Join-Path $bin "BouncyCastle.Cryptography.dll"
if (Test-Path -LiteralPath $bc) {
    Copy-Item -LiteralPath $bc -Destination (Join-Path $stage "BouncyCastle.Cryptography.dll") -Force
} else {
    throw "BouncyCastle.Cryptography.dll missing from build output; the plugin cannot load without it. Build with -p:CopyLocalLockFileAssemblies=true."
}

@"
GWYF Community Network $Version — BepInEx plugin

Install: copy the BepInEx/ folder into the game root (merge with any existing
BepInEx install). The plugin loads automatically next launch.

State (created on first run, under BepInEx/gwyf-community/):
  observer_identity.json   Ed25519 keypair + observer id (delete to reset)
  observer-id.txt          the public observer id (for support questions)
  observation_queue.jsonl  crash-tolerant pending-sighting queue
  diagnostics.txt          version/build/hook/network diagnostics

Config (BepInEx/config/com.gwyf.community.cfg, created on first run):
  network.apiBaseUrl       default http://127.0.0.1:3000
  observation.lobbyEnabled observe the vanilla public lobby browser (on)
  queue.*                  flush cadence, batch size, sighting age limits

Compatibility: BepInEx 6 BE.785+ for game build 303 (older BEs crash at boot).
The hook is passive; with the plugin removed the game is byte-for-byte vanilla.
"@ | Set-Content -Path (Join-Path $stage "README.txt") -Encoding utf8

Write-Host "Zipping to $zipPath"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $OutDir "BepInEx") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Done: $zipPath"
Write-Host "Layout:"
Get-ChildItem -Recurse -File (Join-Path $OutDir "BepInEx") | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($OutDir.Length + 1))"
}
