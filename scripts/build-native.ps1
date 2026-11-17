[CmdletBinding()]
param(
    [ValidateSet('Windows','AndroidArm64','AndroidArm','AndroidX64','iOS','iOSSimulatorArm64','iOSSimulatorX64')]
    [string]$Target='Windows',
    [string]$CMakePath='cmake',
    [string]$AndroidNdkDirectory=$env:ANDROID_NDK_HOME
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path -Parent $PSScriptRoot
$vcpkg=Join-Path $root '.tools/vcpkg'
$revision='0df93a05b9de080129e653ec4776d7e14a1b2e4f'
if(!$env:VCPKG_DEFAULT_BINARY_CACHE){$env:VCPKG_DEFAULT_BINARY_CACHE=Join-Path $root '.tools/vcpkg-cache'}
New-Item -ItemType Directory -Force $env:VCPKG_DEFAULT_BINARY_CACHE | Out-Null
function Run([string]$file,[string[]]$arguments) {
    & $file @arguments
    if($LASTEXITCODE -ne 0){throw "$file failed with exit $LASTEXITCODE"}
}
if(!(Test-Path -LiteralPath (Join-Path $vcpkg '.git'))) {
    Run git @('init',$vcpkg)
    Run git @('-C',$vcpkg,'remote','add','origin','https://github.com/microsoft/vcpkg.git')
    Run git @('-C',$vcpkg,'fetch','--depth','1','origin',$revision)
    Run git @('-C',$vcpkg,'checkout','--detach','FETCH_HEAD')
}
$actual=(& git -C $vcpkg rev-parse HEAD).Trim()
if($actual -ne $revision){throw "vcpkg revision must be $revision (found $actual)."}
$nativeOnWindows=[Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)
$bootstrap=if($nativeOnWindows){Join-Path $vcpkg 'bootstrap-vcpkg.bat'}else{Join-Path $vcpkg 'bootstrap-vcpkg.sh'}
$executable=if($nativeOnWindows){Join-Path $vcpkg 'vcpkg.exe'}else{Join-Path $vcpkg 'vcpkg'}
if(!(Test-Path -LiteralPath $executable)){Run $bootstrap @('-disableMetrics')}
if(!(Get-Command $CMakePath -ErrorAction SilentlyContinue)) {
    $vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
    if($nativeOnWindows -and (Test-Path -LiteralPath $vswhere)) {
        $vs=(& $vswhere -latest -products '*' -property installationPath).Trim()
        $CMakePath=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    }
}
$triplet=switch($Target){
    Windows{'x64-windows-static-md'}
    AndroidArm64{'arm64-android'}
    AndroidArm{'arm-android'}
    AndroidX64{'x64-android'}
    iOS{'arm64-watchtorrent-ios'}
    iOSSimulatorArm64{'arm64-watchtorrent-ios-simulator'}
    iOSSimulatorX64{'x64-watchtorrent-ios-simulator'}
}
$rid=switch($Target){
    Windows{'win-x64'}
    AndroidArm64{'android-arm64'}
    AndroidArm{'android-arm'}
    AndroidX64{'android-x64'}
    iOS{'ios-arm64'}
    iOSSimulatorArm64{'iossimulator-arm64'}
    iOSSimulatorX64{'iossimulator-x64'}
}
$native=Join-Path $root 'native'
$build=Join-Path $root "artifacts/native-build/$rid"
$install=Join-Path $root ".tools/vcpkg-installed/$rid"
$output=Join-Path $root "native/artifacts/$rid"
$options=@('--fresh','-S',$native,'-B',$build,"-DCMAKE_TOOLCHAIN_FILE=$vcpkg/scripts/buildsystems/vcpkg.cmake",
    "-DVCPKG_TARGET_TRIPLET=$triplet","-DVCPKG_INSTALLED_DIR=$install",'-DCMAKE_BUILD_TYPE=Release',
    "-DVCPKG_OVERLAY_TRIPLETS=$native/triplets",
    "-DCMAKE_INSTALL_PREFIX=$output",'-DVCPKG_MANIFEST_INSTALL=ON')
if($Target.StartsWith('Android')) {
    if(!$AndroidNdkDirectory){throw 'Set ANDROID_NDK_HOME or provide -AndroidNdkDirectory (NDK r27 or newer).'}
    $env:ANDROID_NDK_HOME=$AndroidNdkDirectory
    $abi=switch($Target){AndroidArm64{'arm64-v8a'} AndroidArm{'armeabi-v7a'} AndroidX64{'x86_64'}}
    $options+=@('-G','Ninja',"-DVCPKG_CHAINLOAD_TOOLCHAIN_FILE=$AndroidNdkDirectory/build/cmake/android.toolchain.cmake",
        "-DANDROID_ABI=$abi",'-DANDROID_PLATFORM=android-28','-DANDROID_STL=c++_static')
    if($nativeOnWindows -and !(Get-Command ninja -ErrorAction SilentlyContinue)) {
        $ninja=Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $CMakePath))) 'Ninja/ninja.exe'
        if(Test-Path -LiteralPath $ninja){$options+="-DCMAKE_MAKE_PROGRAM=$ninja"}
    }
} elseif($Target.StartsWith('iOS')) {
    $sdk=if($Target -eq 'iOS'){'iphoneos'}else{'iphonesimulator'}
    $architecture=if($Target -eq 'iOSSimulatorX64'){'x86_64'}else{'arm64'}
    $options+=@('-G','Ninja','-DCMAKE_SYSTEM_NAME=iOS',"-DCMAKE_OSX_SYSROOT=$sdk",
        "-DCMAKE_OSX_ARCHITECTURES=$architecture",'-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0')
} else {
    $options+=@('-G','Visual Studio 18 2026','-A','x64')
}
Run $CMakePath $options
Run $CMakePath @('--build',$build,'--config','Release','--parallel','4')
$installArguments=@('--install',$build,'--config','Release')
if($Target.StartsWith('Android')){$installArguments+='--strip'}
Run $CMakePath $installArguments
if($Target -eq 'Windows') {
    $ctest=Join-Path (Split-Path -Parent (Get-Command $CMakePath).Source) 'ctest.exe'
    Run $ctest @('--test-dir',$build,'-C','Release','--output-on-failure')
}
if($Target.StartsWith('iOS')) {
    # A static archive does not automatically contain its CMake dependencies.
    $archives=@((Join-Path $output 'libwatchtorrent_libtorrent.a'))
    $archives+=@(Get-ChildItem -LiteralPath (Join-Path $install "$triplet/lib") -Filter '*.a' | ForEach-Object FullName)
    $merged=Join-Path $output 'combined.a'
    Run xcrun (@('libtool','-static','-o',$merged)+$archives)
    Move-Item -LiteralPath $merged -Destination (Join-Path $output 'libwatchtorrent_libtorrent.a') -Force
}
# Carry all dependency license files with the native artifact.
$licenses=Join-Path $output 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
Get-ChildItem -LiteralPath (Join-Path $install "$triplet/share") -Directory | ForEach-Object {
    $copyright=Join-Path $_.FullName 'copyright'
    if(Test-Path -LiteralPath $copyright){Copy-Item -LiteralPath $copyright -Destination (Join-Path $licenses ($_.Name+'.txt'))}
}
Write-Host "Native libtorrent ready: $output"
