param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$ns2Root = Split-Path -Parent $PSScriptRoot
$ns2Version = ([xml](Get-Content -LiteralPath (Join-Path $ns2Root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
$ns2Name = ([xml](Get-Content -LiteralPath (Join-Path $ns2Root 'Directory.Build.props') -Raw)).Project.PropertyGroup.AppDisplayName | Where-Object { $_ }
if (-not $ns2Name) { throw 'AppDisplayName is required' }
if (-not $OutputDirectory) { $OutputDirectory = "artifacts\NS2Pro-v$ns2Version-release" }
$ns2Publish = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $ns2Root $OutputDirectory }
$ns2Publish = [IO.Path]::GetFullPath($ns2Publish)
$ns2Dotnet = Join-Path $ns2Root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $ns2Dotnet)) { $ns2Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$ns2Downloads = Join-Path $ns2Root '.tools\downloads'
New-Item -ItemType Directory -Path $ns2Downloads -Force | Out-Null

function Get-VerifiedDownload($Url, $Destination, $Hash) {
    if (-not (Test-Path -LiteralPath $Destination)) { Invoke-WebRequest -Uri $Url -OutFile $Destination }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Hash) { throw "Download checksum mismatch: $Destination" }
}
function Assert-Signer($Path, $Expected) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "*$Expected*") { throw "Vendor signature did not validate: $Path" }
}

& (Join-Path $PSScriptRoot 'build.ps1') -Publish -OutputDirectory $ns2Publish
$ns2Prerequisites = Get-Content -LiteralPath (Join-Path $ns2Root 'installer\prerequisites.json') -Raw | ConvertFrom-Json
$ns2PrereqFolder = Join-Path $ns2Publish 'Prerequisites'
New-Item -ItemType Directory -Path $ns2PrereqFolder -Force | Out-Null
foreach ($ns2Dependency in $ns2Prerequisites) {
    $ns2DependencyFile = Join-Path $ns2Downloads $ns2Dependency.file
    Get-VerifiedDownload $ns2Dependency.url $ns2DependencyFile $ns2Dependency.sha256
    Assert-Signer $ns2DependencyFile $ns2Dependency.signer
    Copy-Item -LiteralPath $ns2DependencyFile -Destination $ns2PrereqFolder -Force
}
Copy-Item -LiteralPath (Join-Path $ns2Root 'installer\prerequisites.json') -Destination (Join-Path $ns2PrereqFolder 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $ns2Root 'installer\install-info.txt') -Destination (Join-Path $ns2Publish '使用说明.txt') -Force
$ns2LicenseFolder = Join-Path $ns2Publish 'Licenses'
Copy-Item -LiteralPath (Join-Path $ns2Root 'third_party\vigembus\LICENSE.txt') -Destination (Join-Path $ns2LicenseFolder 'ViGEmBus.txt') -Force
Copy-Item -LiteralPath (Join-Path $ns2Root 'third_party\windows_sdk\LICENSE.rtf') -Destination (Join-Path $ns2LicenseFolder 'WindowsSDK.rtf') -Force

# Preserve the license/notice documents shipped with every resolved NuGet dependency.
$ns2NugetOutput = & $ns2Dotnet nuget locals global-packages --list --force-english-output
$ns2Nuget = ($ns2NugetOutput -replace '^global-packages:\s*', '').Trim()
$ns2Locks = @(Join-Path $ns2Root 'src\NS2Pro.App\packages.lock.json'; Join-Path $ns2Root 'src\NS2Pro.Core\packages.lock.json')
$ns2Copied = @{}
foreach ($ns2Lock in $ns2Locks) {
    $ns2LockData = Get-Content -LiteralPath $ns2Lock -Raw | ConvertFrom-Json
    foreach ($ns2Target in $ns2LockData.dependencies.PSObject.Properties) {
        foreach ($ns2Package in $ns2Target.Value.PSObject.Properties) {
            if ($ns2Package.Value.type -eq 'Project' -or -not $ns2Package.Value.resolved) { continue }
            $ns2PackageId = $ns2Package.Name.ToLowerInvariant()
            $ns2PackageKey = "$ns2PackageId/$($ns2Package.Value.resolved)"
            if ($ns2Copied.ContainsKey($ns2PackageKey)) { continue }
            $ns2Copied[$ns2PackageKey] = $true
            $ns2PackagePath = Join-Path $ns2Nuget $ns2PackageKey
            $ns2PackageLicenses = Get-ChildItem -LiteralPath $ns2PackagePath -File | Where-Object { $_.Name -match '^(license|notice|third.party)' }
            if ($ns2PackageLicenses) {
                $ns2Destination = Join-Path $ns2LicenseFolder "Packages\$ns2PackageId"
                New-Item -ItemType Directory -Path $ns2Destination -Force | Out-Null
                $ns2PackageLicenses | Copy-Item -Destination $ns2Destination -Force
            }
        }
    }
}
# Runtime packs and WinRT projection packs are implicit SDK downloads, outside the lock file.
$ns2RuntimeConfig = Get-Content -LiteralPath (Join-Path $ns2Publish 'NS2Pro.runtimeconfig.json') -Raw | ConvertFrom-Json
$ns2RuntimeVersion = ($ns2RuntimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).version
$ns2ImplicitPackages = @("microsoft.netcore.app.runtime.win-x64/$ns2RuntimeVersion")
$ns2Assets = Get-Content -LiteralPath (Join-Path $ns2Root 'src\NS2Pro.App\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($ns2Framework in $ns2Assets.project.frameworks.PSObject.Properties) {
    foreach ($ns2Download in $ns2Framework.Value.downloadDependencies) {
        $ns2DownloadVersion = ($ns2Download.version -replace '^\[|\]$', '').Split(',')[0].Trim()
        $ns2ImplicitPackages += "$($ns2Download.name.ToLowerInvariant())/$ns2DownloadVersion"
    }
}
foreach ($ns2ImplicitPackage in $ns2ImplicitPackages | Select-Object -Unique) {
    $ns2Source = Join-Path $ns2Nuget $ns2ImplicitPackage
    $ns2Destination = Join-Path $ns2LicenseFolder ('Packages\' + $ns2ImplicitPackage.Split('/')[0])
    New-Item -ItemType Directory -Path $ns2Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $ns2Source -File | Where-Object { $_.Name -match '^(license|notice|third.party)' } | Copy-Item -Destination $ns2Destination -Force
    Get-ChildItem -LiteralPath $ns2Source -Filter '*.nuspec' | Copy-Item -Destination $ns2Destination -Force
}
Get-ChildItem -LiteralPath $ns2Publish -File | Where-Object { $_.Extension -eq '.pdb' } | ForEach-Object {
    throw "Release contains development symbols: $($_.Name). Publish into a fresh directory."
}

$ns2Tool = Get-Content -LiteralPath (Join-Path $ns2Root 'installer\toolchain.json') -Raw | ConvertFrom-Json
$ns2ToolPackage = Join-Path $ns2Downloads "tools.innosetup.$($ns2Tool.version).nupkg"
Get-VerifiedDownload $ns2Tool.url $ns2ToolPackage $ns2Tool.sha256
$ns2CompilerFolder = Join-Path $ns2Root ".tools\innosetup-$($ns2Tool.version)"
if (-not (Test-Path -LiteralPath $ns2CompilerFolder)) {
    & $ns2Dotnet nuget verify $ns2ToolPackage --all
    if ($LASTEXITCODE -ne 0) { throw 'Compiler package signature did not validate' }
    Expand-Archive -LiteralPath $ns2ToolPackage -DestinationPath $ns2CompilerFolder
}
$ns2Compiler = Join-Path $ns2CompilerFolder 'tools\ISCC.exe'
Assert-Signer $ns2Compiler $ns2Tool.signer
Copy-Item -LiteralPath (Join-Path $ns2CompilerFolder 'tools\License.txt') -Destination (Join-Path $ns2LicenseFolder 'InnoSetup.txt') -Force

$ns2Inventory = Get-ChildItem -LiteralPath $ns2Publish -Recurse -File | Where-Object { $_.Name -ne 'release-manifest.json' } | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($ns2Publish, $_.FullName).Replace('\', '/'); Size = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
[pscustomobject]@{ Version = $ns2Version; Architecture = 'x64'; MinimumWindowsBuild = 19041; Files = @($ns2Inventory) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ns2Publish 'release-manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'check-release.ps1') -PublishDirectory $ns2Publish
& $ns2Compiler '/Q' "/DPublishDir=$ns2Publish" "/DAppVersion=$ns2Version" "/DAppDisplayName=$ns2Name" (Join-Path $ns2Root 'installer\NS2Pro.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$ns2ReleaseFolder = Join-Path $ns2Root 'artifacts\release'
$ns2Python = Join-Path $ns2Root '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $ns2Python)) { $ns2Python = (Get-Command python -ErrorAction Stop).Source }
$ns2NativeAudit = & $ns2Python (Join-Path $ns2Root 'tools\audit_pe_imports.py') $ns2Publish
if ($LASTEXITCODE -ne 0) { throw 'Native dependency audit failed' }
$ns2NativeAudit | Set-Content -LiteralPath (Join-Path $ns2ReleaseFolder "NS2Pro-$ns2Version-native-imports.json") -Encoding utf8
if (($ns2NativeAudit | ConvertFrom-Json).PSObject.Properties | Where-Object { $_.Value | Where-Object { -not $_.bundled } }) { throw 'Unbundled MSVC runtime dependency found; update prerequisite handling before release' }
$ns2Zip = Join-Path $ns2ReleaseFolder "NS2Pro-$ns2Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $ns2Publish '*') -DestinationPath $ns2Zip -CompressionLevel Optimal -Force
$ns2Setup = Join-Path $ns2ReleaseFolder "NS2Pro-$ns2Version-win-x64-setup.exe"
$ns2Outputs = @($ns2Setup, $ns2Zip) | ForEach-Object { [pscustomobject]@{ File = Split-Path -Leaf $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } }
$ns2Outputs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ns2ReleaseFolder "NS2Pro-$ns2Version-checksums.json") -Encoding utf8
$ns2Outputs | Format-Table -AutoSize
