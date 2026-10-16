param([switch]$Publish, [string]$OutputDirectory = 'artifacts\NS2Pro')
$ErrorActionPreference = 'Stop'
$ns2Root = Split-Path -Parent $PSScriptRoot
$ns2Dotnet = Join-Path $ns2Root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $ns2Dotnet)) { $ns2Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $ns2Root
try {
    & $ns2Dotnet restore NS2Pro.sln -r win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed' }
    & $ns2Dotnet run --project tests\NS2Pro.Core.Tests -c Release -r win-x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Protocol checks failed' }
    if ($Publish) {
        & $ns2Dotnet publish src\NS2Pro.App -c Release -r win-x64 --self-contained true --no-restore -o $OutputDirectory
    } else {
        & $ns2Dotnet build src\NS2Pro.App -c Debug -r win-x64 --no-restore
    }
    if ($LASTEXITCODE -ne 0) { throw 'Native app build failed' }
    if ($Publish) {
        foreach ($ns2Required in @('NS2Pro.exe', 'NS2Pro.dll', 'NS2Pro.pri', 'ViGEmClient.dll', 'Assets\App.ico', 'Assets\App.png', 'Assets\App.svg', 'Licenses\ViGEmClient.txt', 'Licenses\vgamepad.txt')) {
            $ns2RequiredPath = Join-Path $OutputDirectory $ns2Required
            if (-not (Test-Path -LiteralPath $ns2RequiredPath)) { throw "Published file missing: $ns2Required" }
        }
        # Publishing into an old directory does not remove dependencies from the old package graph.
        foreach ($ns2Unused in @('onnxruntime.dll', 'DirectML.dll', 'Microsoft.Windows.AI.MachineLearning.dll', 'Microsoft.Windows.Search.dll', 'Microsoft.Windows.Widgets.dll', 'DWriteCore.dll')) {
            if (Test-Path -LiteralPath (Join-Path $OutputDirectory $ns2Unused)) {
                throw "Unused SDK component remains in publish output: $ns2Unused. Publish into a fresh directory."
            }
        }
    }
} finally { Pop-Location }
