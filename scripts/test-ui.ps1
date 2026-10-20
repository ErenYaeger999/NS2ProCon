param([string]$OutputDirectory = 'artifacts\checks\ui-tests')
$ErrorActionPreference = 'Stop'
$ns2Root = Split-Path -Parent $PSScriptRoot
$ns2Dotnet = Join-Path $ns2Root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $ns2Dotnet)) { $ns2Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $ns2Root
try {
    $ns2Output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $ns2Root $OutputDirectory }
    & $ns2Dotnet restore tests\NS2Pro.UI.Tests -r win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'UI dependency restore failed' }
    & $ns2Dotnet publish tests\NS2Pro.UI.Tests -c Release -r win-x64 --no-restore -o $ns2Output
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed' }
    $ns2Test = Start-Process -FilePath (Join-Path $ns2Output 'NS2Pro.exe') -ArgumentList '--preview-onboarding','--capture-enhancement','--capture-motion' -WindowStyle Hidden -PassThru
    if (-not $ns2Test.WaitForExit(60000)) { $ns2Test.Kill(); throw 'UI tests exceeded 60 seconds' }
    $ns2Result = Get-Content -LiteralPath (Join-Path $ns2Output 'dependency-smoke-result.json') -Raw | ConvertFrom-Json
    if ($ns2Test.ExitCode -ne 0 -or $ns2Result.Error) { throw "UI checks failed: $($ns2Result.Error)" }
    Write-Output "PASS: $($ns2Result.Passed.Count) real WinUI checks. No physical controller was connected."
} finally { Pop-Location }
