param([Parameter(Mandatory = $true)][string]$PublishDirectory, [string]$ReportDirectory = '')
$ErrorActionPreference = 'Stop'
$ns2Directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$ns2Checks = [Collections.Generic.List[string]]::new()
foreach ($ns2Required in @('NS2Pro.exe','NS2Pro.dll','NS2Pro.Core.dll','NS2Pro.pri','NS2Pro.runtimeconfig.json','hostfxr.dll','coreclr.dll','Microsoft.UI.Xaml.dll','Microsoft.WindowsAppRuntime.dll','LICENSE.txt','ViGEmClient.dll','Assets\App.ico','Assets\App.png','Assets\App.svg','Licenses\ViGEmClient.txt','Licenses\vgamepad.txt','Licenses\ViGEmBus.txt','Licenses\WindowsSDK.rtf','Licenses\Packages\microsoft.netcore.app.runtime.win-x64\LICENSE.TXT','Prerequisites\manifest.json','使用说明.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ns2Directory $ns2Required) -PathType Leaf)) { throw "Missing release file: $ns2Required" }
}
$ns2Checks.Add('Required app, runtime, resource, driver and license files are present')
if ((Get-FileHash -LiteralPath (Join-Path $ns2Directory 'LICENSE.txt') -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'LICENSE') -Algorithm SHA256).Hash) { throw 'Release project license differs from the repository LICENSE' }
$ns2Checks.Add('Project MIT license matches the repository LICENSE')
$ns2Runtime = Get-Content -LiteralPath (Join-Path $ns2Directory 'NS2Pro.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($ns2Runtime.runtimeOptions.framework -or $ns2Runtime.runtimeOptions.frameworks -or -not $ns2Runtime.runtimeOptions.includedFrameworks) { throw 'Release still depends on a globally installed .NET runtime' }
$ns2Checks.Add('.NET runtime is self-contained')
foreach ($ns2Unused in @('onnxruntime.dll','DirectML.dll','Microsoft.Windows.AI.MachineLearning.dll','Microsoft.Windows.Search.dll','Microsoft.Windows.Widgets.dll','DWriteCore.dll')) {
    if (Test-Path -LiteralPath (Join-Path $ns2Directory $ns2Unused)) { throw "Unused SDK component in release: $ns2Unused. Use a fresh publish directory." }
}
$ns2Checks.Add('Unused SDK AI, ML, Search, Widgets and DWrite components are absent')
foreach ($ns2Forbidden in @('settings.json','session.log','.env','Start-PC.cmd')) {
    if (Test-Path -LiteralPath (Join-Path $ns2Directory $ns2Forbidden)) { throw "Personal or development file in release: $ns2Forbidden" }
}
$ns2Checks.Add('No user settings or session logs are shipped')
$ns2Manifest = Get-Content -LiteralPath (Join-Path $ns2Directory 'release-manifest.json') -Raw | ConvertFrom-Json
foreach ($ns2Entry in $ns2Manifest.Files) {
    $ns2FilePath = [IO.Path]::GetFullPath((Join-Path $ns2Directory $ns2Entry.Path))
    if (-not $ns2FilePath.StartsWith($ns2Directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release inventory contains an out-of-folder path' }
    if (-not (Test-Path -LiteralPath $ns2FilePath) -or (Get-FileHash -LiteralPath $ns2FilePath -Algorithm SHA256).Hash -ne $ns2Entry.Sha256) { throw "Release file checksum mismatch: $($ns2Entry.Path)" }
}
$ns2Checks.Add("All $($ns2Manifest.Files.Count) inventoried payload files match SHA-256")
$ns2Prereqs = Get-Content -LiteralPath (Join-Path $ns2Directory 'Prerequisites\manifest.json') -Raw | ConvertFrom-Json
foreach ($ns2Dependency in $ns2Prereqs) {
    $ns2Installer = Join-Path $ns2Directory "Prerequisites\$($ns2Dependency.file)"
    if ((Get-FileHash -LiteralPath $ns2Installer -Algorithm SHA256).Hash -ne $ns2Dependency.sha256) { throw 'Prerequisite installer checksum mismatch' }
    $ns2Signature = Get-AuthenticodeSignature -LiteralPath $ns2Installer
    if ($ns2Signature.Status -ne 'Valid' -or $ns2Signature.SignerCertificate.Subject -notlike "*$($ns2Dependency.signer)*") { throw 'Prerequisite vendor signature did not validate' }
}
$ns2Checks.Add('Bundled vendor installer digest and Authenticode signature validate')
$ns2AssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ns2Directory 'NS2Pro.dll')).Version.ToString(3)
if ($ns2AssemblyVersion -ne $ns2Manifest.Version) { throw 'Release version mismatch' }
$ns2Checks.Add('App and release inventory versions match')
$ns2PayloadSize = [long](Get-ChildItem -LiteralPath $ns2Directory -Recurse -File | Measure-Object Length -Sum).Sum
$ns2Result = [pscustomobject]@{
    Version = $ns2Manifest.Version
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Passed = @($ns2Checks)
    PayloadSizeBytes = $ns2PayloadSize
    PayloadSizeMiB = [Math]::Round($ns2PayloadSize / 1MB, 1)
    AppSignature = (Get-AuthenticodeSignature -LiteralPath (Join-Path $ns2Directory 'NS2Pro.exe')).Status.ToString()
    RemainingValidation = @('Final visual and controller regression on the release payload','Clean Windows 10 x64 install and first BLE connection','Clean Windows 11 x64 install and first BLE connection','Driver installation on a machine without ViGEmBus','Publisher code signing before broad public distribution')
}
$ns2ReportFolder = if ($ReportDirectory) { $ReportDirectory } else { Join-Path (Split-Path -Parent $ns2Directory) 'release' }
New-Item -ItemType Directory -Path $ns2ReportFolder -Force | Out-Null
$ns2Result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ns2ReportFolder "NS2Pro-$($ns2Manifest.Version)-check-report.json") -Encoding utf8
Write-Output "PASS: $($ns2Checks.Count) release checks ($($ns2Manifest.Files.Count) payload files)."
