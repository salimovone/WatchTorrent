[CmdletBinding()]
param(
    [ValidateSet('Tests', 'Windows', 'WindowsInstaller', 'Android', 'iOSSimulator')]
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

function Copy-VisualCppRuntime {
    param([string] $OutputDirectory)

    $redistRoot = $env:VCToolsRedistDir
    if (!$redistRoot) {
        $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (!(Test-Path -LiteralPath $vswhere)) {
            throw 'Install the Visual Studio Desktop development with C++ workload to build the Windows installer.'
        }
        $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (!$installation) { throw 'Visual Studio x64 C++ tools were not found.' }
        $redistRoot = Get-ChildItem -LiteralPath (Join-Path $installation 'VC/Redist/MSVC') -Directory |
            Where-Object Name -Match '^\d+(\.\d+)+$' |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (!$redistRoot) { throw 'The Visual C++ Redistributable directory was not found.' }

    $runtime = Get-ChildItem -LiteralPath (Join-Path $redistRoot 'x64') -Directory -Filter 'Microsoft.VC*.CRT' |
        Select-Object -First 1
    if (!$runtime -or !(Test-Path -LiteralPath (Join-Path $runtime.FullName 'vcruntime140.dll'))) {
        throw 'The x64 Visual C++ runtime DLLs were not found.'
    }
    Get-ChildItem -LiteralPath $runtime.FullName -File -Filter '*.dll' |
        Copy-Item -Destination $OutputDirectory
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

    $framework = switch -Regex ($Target) {
        '^Windows(Installer)?$' { 'net10.0-windows10.0.19041.0' }
        'Android' { 'net10.0-android' }
        'iOSSimulator' { 'net10.0-ios' }
    }
    $workload = switch -Regex ($Target) {
        '^Windows(Installer)?$' { 'maui-windows' }
        'Android' { 'maui-android' }
        'iOSSimulator' { 'maui-ios' }
    }
    if ($Target -in @('Windows', 'WindowsInstaller') -and ![Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { throw 'Windows packaging requires Windows.' }
    if ($Target -eq 'iOSSimulator' -and ![Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { throw 'iOS packaging requires macOS and Xcode.' }
    if ($RestoreWorkloads) {
        Invoke-DotNet -Arguments @('workload', 'install', $workload, '--skip-manifest-update')
    }

    $output = if ($Target -eq 'WindowsInstaller') {
        Join-Path $repoRoot 'artifacts/windows-installer/payload'
    } else {
        Join-Path $repoRoot "artifacts/$($Target.ToLowerInvariant())"
    }
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
    switch -Regex ($Target) {
        '^Windows(Installer)?$' {
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
    switch -Regex ($Target) {
        '^Windows(Installer)?$' {
            if (!(Test-Path -LiteralPath (Join-Path $output 'WatchTorrent.exe'))) { throw 'Windows executable was not produced.' }
            if ($Target -eq 'WindowsInstaller') {
                Copy-VisualCppRuntime -OutputDirectory $output
                $installerOutput = Split-Path -Parent $output
                $appProject = [xml](Get-Content -LiteralPath 'WatchTorrent/WatchTorrent.csproj' -Raw)
                $version = $appProject.SelectSingleNode('/Project/PropertyGroup/ApplicationDisplayVersion').InnerText
                Invoke-DotNet -Arguments @('build', 'WatchTorrent.Installer/Bundle/WatchTorrentSetup.wixproj',
                    '-c', $Configuration, '--nologo', '-o', $installerOutput, "-p:WatchTorrentVersion=$version",
                    "-p:WatchTorrentPublishDir=$output")
                if (!(Test-Path -LiteralPath (Join-Path $installerOutput "WatchTorrent-$version-windows-x64-setup.exe"))) {
                    throw 'WiX setup executable was not produced.'
                }
            }
        }
        'Android' {
            $signedApks = @(Get-ChildItem -LiteralPath $output -Filter '*-Signed.apk' -Recurse)
            if (!$signedApks.Count) { throw 'Installable signed APK was not produced.' }
            $abi = switch ($AndroidRid) {
                'android-arm64' { 'arm64-v8a' }
                'android-arm' { 'armeabi-v7a' }
                'android-x64' { 'x86_64' }
            }
            foreach ($apk in $signedApks) {
                $archive = [IO.Compression.ZipFile]::OpenRead($apk.FullName)
                try {
                    foreach ($library in @('libvlc.so', 'libc++_shared.so', 'libwatchtorrent_libtorrent.so')) {
                        if (!$archive.GetEntry("lib/$abi/$library")) {
                            throw "APK is missing lib/$abi/$library; playback would fail on the device."
                        }
                    }
                }
                finally { $archive.Dispose() }
            }
        }
        'iOSSimulator' {
            if (!(Get-ChildItem -Path 'WatchTorrent/bin' -Directory -Filter '*.app' -Recurse)) { throw 'iOS simulator app was not produced.' }
        }
    }
}
finally { Pop-Location }
