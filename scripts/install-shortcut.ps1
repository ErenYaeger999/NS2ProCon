param([string]$AppDirectory = 'artifacts\NS2Pro')
$ErrorActionPreference = 'Stop'
$ns2Root = Split-Path -Parent $PSScriptRoot
$ns2Name = ([xml](Get-Content -LiteralPath (Join-Path $ns2Root 'Directory.Build.props') -Raw)).Project.PropertyGroup.AppDisplayName | Where-Object { $_ }
if (-not $ns2Name) { throw '应用名称未配置' }
$ns2AppPath = if ([IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $ns2Root $AppDirectory }
$ns2Executable = [IO.Path]::GetFullPath((Join-Path $ns2AppPath 'NS2Pro.exe'))
if (-not (Test-Path -LiteralPath $ns2Executable)) { throw '请先运行 scripts/build.ps1 -Publish' }
$ns2Desktop = [Environment]::GetFolderPath('Desktop')
$ns2ShortcutPath = Join-Path $ns2Desktop "$ns2Name.lnk"
$ns2Shell = New-Object -ComObject WScript.Shell
$ns2Shortcut = $ns2Shell.CreateShortcut($ns2ShortcutPath)
$ns2Shortcut.TargetPath = $ns2Executable
$ns2Shortcut.Arguments = ''
$ns2Shortcut.WorkingDirectory = Split-Path -Parent $ns2Executable
$ns2Shortcut.IconLocation = (Join-Path (Split-Path -Parent $ns2Executable) 'Assets\App.ico') + ',0'
$ns2Shortcut.Description = "$ns2Name · 无线连接、手柄测试与映射预设"
$ns2Shortcut.Save()
foreach ($ns2OldName in @('NS2 Pro 无线连接 PC.lnk', 'NS2 Pro.lnk')) {
    $ns2OldPath = Join-Path $ns2Desktop $ns2OldName
    if (Test-Path -LiteralPath $ns2OldPath -PathType Leaf) {
        $ns2OldLink = $ns2Shell.CreateShortcut($ns2OldPath)
        if ([IO.Path]::GetFileName($ns2OldLink.TargetPath) -eq 'NS2Pro.exe') { Remove-Item -LiteralPath $ns2OldPath }
    }
}
Write-Output $ns2ShortcutPath
