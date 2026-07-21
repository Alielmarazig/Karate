# Builds the Karate MSI installer.
# Prerequisites (one-time):
#   dotnet tool install --global wix --version 5.0.2
#   wix extension add -g WixToolset.UI.wixext/5.0.2

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = "0.2.0"   # keep in sync with <Version> in Karate.csproj and Karate.wxs

Write-Host "Publishing self-contained release build..." -ForegroundColor Cyan
dotnet publish "$root\Karate.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o "$root\publish"

Write-Host "Building MSI..." -ForegroundColor Cyan
wix build "$root\installer\Karate.wxs" `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -d PublishDir="$root\publish" `
    -d LicenseRtf="$root\installer\License.rtf" `
    -d IconFile="$root\Assets\karate.ico" `
    -o "$root\installer\Karate-$version-x64.msi"

Write-Host "Done: $root\installer\Karate-$version-x64.msi" -ForegroundColor Green
