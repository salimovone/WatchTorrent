[CmdletBinding()]
param(
    [ValidateSet('Tests', 'Windows', 'Android', 'iOSSimulator')]
    [string] $Target = 'Tests',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $RestoreWorkloads,
    [string] $DotNetPath = 'dotnet',
    [string] $AndroidSdkDirectory = $env:ANDROID_HOME,
    [string] $JavaSdkDirectory = $env:JAVA_HOME,
    [ValidateSet('android-arm64','android-arm','android-x64')]
    [string] $AndroidRid = 'android-arm64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot

function Invoke-DotNet {
    param([string[]] $Arguments)
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
}

try {
    Invoke-DotNet -Arguments @('--version')
    if ($Target -eq 'Tests') {
        $tests = 'WatchTorrent.EngineTests/WatchTorrent.EngineTests.csproj'
        Invoke-DotNet -Arguments @('build', $tests, '-c', $Configuration, '--nologo', '-warnaserror')
        # This project is an executable assertion suite, not a dotnet-test adapter.
        Invoke-DotNet -Arguments @('run', '--project', $tests, '-c', $Configuration, '--no-build', '--no-restore')
        return
    }

    $framework = switch ($Target) {
        'Windows' { 'net10.0-windows10.0.19041.0' }
        'Android' { 'net10.0-android' }
        'iOSSimulator' { 'net10.0-ios' }
    }
    $workload = switch ($Target) {
        'Windows' { 'maui-windows' }
        'Android' { 'maui-android' }
        'iOSSimulator' { 'maui-ios' }
    }
    if ($Target -eq 'Windows' -and ![Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { throw 'Windows packaging requires Windows.' }
    if ($Target -eq 'iOSSimulator' -and ![Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { throw 'iOS packaging requires macOS and Xcode.' }
    if ($RestoreWorkloads) {
        Invoke-DotNet -Arguments @('workload', 'install', $workload, '--skip-manifest-update')
    }

    $output = Join-Path $repoRoot "artifacts/$($Target.ToLowerInvariant())"
    # Publish must not retain DLLs from a previous engine or architecture.
    if(Test-Path -LiteralPath $output) {
        $resolved=[IO.Path]::GetFullPath($output)
        $artifactRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
        if(!$resolved.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid artifact output path.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $arguments = @('publish', 'WatchTorrent/WatchTorrent.csproj', '-c', $Configuration,
        "-p:WatchTorrentTarget=$framework", '-f', $framework, '--nologo', '-o', $output,
        "-bl:$repoRoot/artifacts/$Target.binlog")
    New-Item -ItemType Directory -Force (Join-Path $repoRoot 'artifacts') | Out-Null
    switch ($Target) {
        'Windows' {
            $arguments += @('-r', 'win-x64', '-p:SelfContained=true', '-p:WindowsAppSDKSelfContained=true',
                '-p:WindowsPackageType=None', '-p:PublishTrimmed=false')
        }
        'Android' {
            # A development-signed APK for installation/testing; store signing needs a release keystore.
            $arguments += @('-r', $AndroidRid, '-p:AndroidPackageFormats=apk', '-p:AcceptAndroidSdkLicenses=true')
            if ($AndroidSdkDirectory) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
            if ($JavaSdkDirectory) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
        }
        'iOSSimulator' {
            $rid = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'iossimulator-arm64' } else { 'iossimulator-x64' }
            $arguments += @('-r', $rid, '-p:EnableCodeSigning=false', '-p:BuildIpa=false')
        }
    }
    Invoke-DotNet -Arguments $arguments
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $output
    switch ($Target) {
        'Windows' {
            if (!(Test-Path -LiteralPath (Join-Path $output 'WatchTorrent.exe'))) { throw 'Windows executable was not produced.' }
        }
        'Android' {
            if (!(Get-ChildItem -LiteralPath $output -Filter '*-Signed.apk' -Recurse)) { throw 'Installable signed APK was not produced.' }
        }
        'iOSSimulator' {
            if (!(Get-ChildItem -Path 'WatchTorrent/bin' -Directory -Filter '*.app' -Recurse)) { throw 'iOS simulator app was not produced.' }
        }
    }
}
finally { Pop-Location }
