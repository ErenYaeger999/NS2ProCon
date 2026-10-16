param([string]$AppDirectory = 'artifacts\NS2Pro')
$ErrorActionPreference = 'Stop'
$ns2Root = Split-Path -Parent $PSScriptRoot
$ns2AppPath = if ([IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $ns2Root $AppDirectory }
$ns2Executable = [IO.Path]::GetFullPath((Join-Path $ns2AppPath 'NS2Pro.exe'))
if (-not (Test-Path -LiteralPath $ns2Executable)) { throw '请先运行 scripts/build.ps1 -Publish' }
$ns2Desktop = [Environment]::GetFolderPath('Desktop')
$ns2ShortcutPath = Join-Path $ns2Desktop 'NS2 Pro 无线连接 PC.lnk'
$ns2Shell = New-Object -ComObject WScript.Shell
$ns2Shortcut = $ns2Shell.CreateShortcut($ns2ShortcutPath)
$ns2Shortcut.TargetPath = $ns2Executable
$ns2Shortcut.Arguments = ''
$ns2Shortcut.WorkingDirectory = Split-Path -Parent $ns2Executable
$ns2Shortcut.IconLocation = (Join-Path $ns2Root 'src\NS2Pro.App\Assets\App.ico') + ',0'
$ns2Shortcut.Description = 'NS2 Pro · 原生连接、手柄测试与映射预设'
$ns2Shortcut.Save()
Write-Output $ns2ShortcutPath
