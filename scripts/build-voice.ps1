[CmdletBinding()]
param(
    [ValidateSet('Android', 'Windows', 'Both')][string] $Target = 'Both',
    [string] $CMakePath = 'cmake'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$cacheRoot = Join-Path $repoRoot '.tools/voice-downloads'
$artifactRoot = Join-Path $repoRoot 'native/artifacts/voice'
New-Item -ItemType Directory -Force $cacheRoot, $artifactRoot, (Join-Path $artifactRoot 'licenses') | Out-Null
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'native/voice/upstream.json') -Raw | ConvertFrom-Json
function Get-VerifiedVoicePackage {
    param([string] $Url, [string] $Hash, [string] $FileName)
    $package = Join-Path $cacheRoot $FileName
    if (!(Test-Path -LiteralPath $package) -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $Hash) {
        & curl.exe --fail --location --silent --show-error --retry 3 --max-time 300 --output $package $Url
        if ($LASTEXITCODE -ne 0) { throw "Voice dependency download failed: $FileName" }
    }
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $Hash) { throw "Voice dependency SHA256 mismatch: $FileName" }
    return $package
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'native/voice/LICENSE.ntgcalls.txt'), (Join-Path $repoRoot 'native/voice/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $artifactRoot 'licenses')
if ($Target -in @('Android', 'Both')) {
    $aar = Get-VerifiedVoicePackage $lock.android.url $lock.android.sha256 'ntgcalls-3.0.7.aar'
    Copy-Item -LiteralPath $aar -Destination (Join-Path $artifactRoot 'ntgcalls-3.0.7.aar')
    Write-Host 'VoiceConnection Android AAR verified and staged.'
}
if ($Target -in @('Windows', 'Both')) {
    $archive = Get-VerifiedVoicePackage $lock.windows.url $lock.windows.sha256 'ntgcalls-3.0.7-windows.zip'
    $packageRoot = Join-Path $cacheRoot 'windows-3.0.7'
    Expand-Archive -LiteralPath $archive -DestinationPath $packageRoot -Force
    if (!(Get-Command $CMakePath -ErrorAction SilentlyContinue)) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $CMakePath = & $vswhere -latest -products '*' -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
    }
    if (!$CMakePath) { throw 'CMake/MSVC x64 build tools are required.' }
    $buildRoot = Join-Path $cacheRoot 'windows-build'
    & $CMakePath -S (Join-Path $repoRoot 'native/voice') -B $buildRoot -A x64 "-DVOICE_NATIVE_PACKAGE=$($packageRoot.Replace('\','/'))"
    if ($LASTEXITCODE -ne 0) { throw 'VoiceConnection CMake configure failed.' }
    & $CMakePath --build $buildRoot --config Release --parallel
    if ($LASTEXITCODE -ne 0) { throw 'VoiceConnection native build failed.' }
    $destination = Join-Path $artifactRoot 'win-x64'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -Path (Join-Path $buildRoot 'Release/*.dll') -Destination $destination
    Write-Host 'VoiceConnection Windows native DLLs built and staged.'
}
