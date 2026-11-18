param([switch]$Test, [switch]$DesktopTest, [switch]$HotkeyTest, [switch]$StartupTest, [switch]$ChooserTest, [switch]$Package)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet restore MemoryCapture.sln --disable-parallel -m:1
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
dotnet build MemoryCapture.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test -or $DesktopTest) {
    & './tests/MemoryCapture.Tests/bin/Release/net10.0-windows10.0.19041.0/MemoryCapture.Tests.exe' test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($DesktopTest) {
    & './tests/MemoryCapture.Tests/bin/Release/net10.0-windows10.0.19041.0/MemoryCapture.Tests.exe' --desktop
    if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed.' }
}
if ($HotkeyTest) {
    & './tests/MemoryCapture.Tests/bin/Release/net10.0-windows10.0.19041.0/MemoryCapture.Tests.exe' --hotkeys
    if ($LASTEXITCODE -ne 0) { throw 'Hotkey tests failed.' }
}
if ($StartupTest) {
    & './tests/MemoryCapture.Tests/bin/Release/net10.0-windows10.0.19041.0/MemoryCapture.Tests.exe' --startup-tests
    if ($LASTEXITCODE -ne 0) { throw 'Startup tests failed.' }
}
if ($ChooserTest) {
    & './tests/MemoryCapture.Tests/bin/Release/net10.0-windows10.0.19041.0/MemoryCapture.Tests.exe' --chooser-tests
    if ($LASTEXITCODE -ne 0) { throw 'Capture chooser tests failed.' }
}
if ($Package) {
    $version = ([xml](Get-Content 'src/MemoryCapture/MemoryCapture.csproj' -Raw)).Project.PropertyGroup.Version
    $packageName = "MemoryCapture-$version-win-x64"
    $destination = Join-Path $PSScriptRoot "dist/$packageName"
    dotnet publish src/MemoryCapture/MemoryCapture.csproj -c Release -r win-x64 --self-contained true -o $destination -m:1 -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination $destination
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $destination
    Copy-Item -LiteralPath CHANGELOG.md -Destination $destination
    Copy-Item -LiteralPath VERIFICATION.md -Destination $destination
    Copy-Item -LiteralPath licenses -Destination $destination -Recurse -Force
    $frameworks = (Get-Content "$destination/MemoryCapture.runtimeconfig.json" -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
    foreach ($framework in $frameworks) {
        $runtimePackageName = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
        $packageRoot = Join-Path $PSScriptRoot ".packages/$runtimePackageName/$($framework.version)"
        $licensePath = if (Test-Path -LiteralPath "$packageRoot/LICENSE.TXT") { "$packageRoot/LICENSE.TXT" } else { "$packageRoot/LICENSE" }
        Copy-Item -LiteralPath $licensePath -Destination "$destination/licenses/$($framework.name)-LICENSE.txt"
        if (Test-Path -LiteralPath "$packageRoot/THIRD-PARTY-NOTICES.TXT") {
            Copy-Item -LiteralPath "$packageRoot/THIRD-PARTY-NOTICES.TXT" -Destination "$destination/licenses/$($framework.name)-NOTICES.txt"
        }
    }
    Compress-Archive -Path "$destination/*" -DestinationPath "dist/$packageName.zip" -Force
    Write-Host "Portable app: dist/$packageName.zip"
}
