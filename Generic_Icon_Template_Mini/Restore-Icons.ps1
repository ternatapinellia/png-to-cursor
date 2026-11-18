param(
    [switch]$NoExplorerRestart
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`""
    )
    if ($NoExplorerRestart) {
        $arguments += '-NoExplorerRestart'
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    exit
}

$Root = $PSScriptRoot
$BackupRoot = Join-Path $Root 'BACKUP'
$RunBackup = Join-Path $BackupRoot ('restore-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $RunBackup -Force | Out-Null
$LogPath = Join-Path $RunBackup 'restore.log'

function Write-Log {
    param([string]$Message)
    Write-Host $Message
    Add-Content -LiteralPath $LogPath -Value $Message -Encoding UTF8
}

function Remove-KeyTree {
    param(
        [string]$Path,
        [switch]$Machine
    )

    $root = if ($Machine) {
        [Microsoft.Win32.Registry]::LocalMachine
    } else {
        [Microsoft.Win32.Registry]::CurrentUser
    }

    try {
        $root.DeleteSubKeyTree($Path, $false)
    } catch {
        # The key may be absent or protected. Continue with the other reset steps.
    }
}

function Set-DefaultValue {
    param(
        [string]$Path,
        [string]$Value,
        [switch]$Machine
    )

    try {
        $root = if ($Machine) {
            [Microsoft.Win32.Registry]::LocalMachine
        } else {
            [Microsoft.Win32.Registry]::CurrentUser
        }
        $key = $root.CreateSubKey($Path)
        try {
            $key.SetValue('', $Value, [Microsoft.Win32.RegistryValueKind]::String)
        } finally {
            $key.Dispose()
        }
    } catch {
        Write-Log "Skipped protected key: $Path"
    }
}

function Remove-Value {
    param(
        [string]$Path,
        [string]$Name,
        [switch]$Machine
    )

    try {
        $root = if ($Machine) {
            [Microsoft.Win32.Registry]::LocalMachine
        } else {
            [Microsoft.Win32.Registry]::CurrentUser
        }
        $key = $root.OpenSubKey($Path, $true)
        if ($key) {
            try {
                $key.DeleteValue($Name, $false)
            } finally {
                $key.Dispose()
            }
        }
    } catch {
        Write-Log "Skipped protected value: $Path\$Name"
    }
}

function Remove-CustomDefaultIcon {
    param(
        [string]$Path,
        [switch]$Machine
    )

    try {
        $root = if ($Machine) {
            [Microsoft.Win32.Registry]::LocalMachine
        } else {
            [Microsoft.Win32.Registry]::CurrentUser
        }
        $key = $root.OpenSubKey($Path, $false)
        if (-not $key) {
            return
        }

        try {
            $value = [string]$key.GetValue('', '')
            if ($value -match 'GenericIconTemplate|IconTools|PSD-2026|Folder\.ico|FolderEmpty\.ico|Archive\.ico|PSD\.ico') {
                $root.DeleteSubKeyTree($Path, $false)
            }
        } finally {
            $key.Dispose()
        }
    } catch {
        Write-Log "Skipped custom icon key: $Path"
    }
}

Write-Log 'Restoring Windows default system icons...'
$explorerClsid = 'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID'
foreach ($clsid in @(
    '{20D04FE0-3AEA-1069-A2D8-08002B30309D}',
    '{645FF040-5081-101B-9F08-00AA002F954E}',
    '{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}',
    '{59031A47-3F72-44A7-89C5-5595FE6B30EE}'
)) {
    Remove-KeyTree "$explorerClsid\$clsid\DefaultIcon"
}

Write-Log 'Restoring folder icons...'
foreach ($className in @(
    'Directory', 'Folder', 'LibraryFolder', 'UserLibraryFolder',
    'UserFiles', 'UserProfile', 'AllUsersProfile', 'Public',
    'UsersFilesFolder', 'SearchFolder'
)) {
    Remove-CustomDefaultIcon "Software\Classes\$className\DefaultIcon"
}

Set-DefaultValue 'SOFTWARE\Classes\Directory\DefaultIcon' '%SystemRoot%\System32\shell32.dll,3' -Machine
Set-DefaultValue 'SOFTWARE\Classes\Folder\DefaultIcon' '%SystemRoot%\System32\shell32.dll,3' -Machine

$shellIconsPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
foreach ($valueName in @('3', '4', '50', '51', '52', '53')) {
    Remove-Value $shellIconsPath $valueName
    Remove-Value $shellIconsPath $valueName -Machine
}

Write-Log 'Restoring file association icons...'
foreach ($extension in @(
    '.doc', '.docx', '.docm', '.dot', '.dotx', '.dotm',
    '.xlsx', '.xls', '.xlsm', '.pdf', '.txt',
    '.png', '.jpg', '.jpeg', '.psd', '.ico',
    '.ppt', '.pptx', '.pptm', '.pps', '.ppsx', '.ppsm', '.pot', '.potx', '.potm',
    '.zip', '.rar', '.7z', '.tar', '.gz', '.bz2', '.xz', '.tgz', '.cab', '.iso'
)) {
    Remove-CustomDefaultIcon "Software\Classes\$extension\DefaultIcon"
}

foreach ($progId in @(
    'Word.Document.12', 'Word.Document.8', 'Word.Template.12',
    'WPS.Doc.6', 'WPS.Docx.6', 'WPS.Docm.6',
    'WPS.Dot.6', 'WPS.Dotx.6', 'WPS.Dotm.6',
    'Excel.Sheet.12', 'Excel.Sheet.8',
    'ET.Xlsx.6', 'ET.Xls.6', 'ET.Xlsm.6',
    'sogoupdf.exe.pdf', 'txtfilelegacy', 'txtfile',
    'AppX4ztfk9wxr86nxmzzq47px0nh0e58b8fw',
    'Photoshop.Image.19', 'Photoshop.Image.20', 'Photoshop.Image.21',
    'Photoshop.Image.22', 'Photoshop.Image.23', 'Photoshop.Image.24',
    'Photoshop.Image.25', 'Photoshop.Image.26', 'Photoshop.Image.27',
    'Photoshop.Image.28', 'Photoshop.Image.29', 'Photoshop.Image.30',
    'CompressedFolder',
    'wpmain.exe.zip', 'wpmain.exe.rar', 'wpmain.exe.7z',
    'wpmain.exe.tar', 'wpmain.exe.gz', 'wpmain.exe.bz2', 'wpmain.exe.xz',
    'PowerPoint.Show.12', 'PowerPoint.Show.8', 'PowerPoint.Template.12',
    'WPP.Ppt.6', 'WPP.Pptx.6', 'WPP.Pptm.6',
    'WPP.Pps.6', 'WPP.Ppsx.6', 'WPP.Ppsm.6',
    'WPP.Pot.6', 'WPP.Potx.6', 'WPP.Potm.6'
)) {
    Remove-CustomDefaultIcon "Software\Classes\$progId\DefaultIcon"
}

# Remove the dedicated classes created by the installer.
Remove-KeyTree 'Software\Classes\IconTools.ZipArchive'
Remove-KeyTree 'Software\Classes\IconTools.PhotoshopDocument'

$zipKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.zip', $true)
if ($zipKey) {
    try {
        if ([string]$zipKey.GetValue('', '') -eq 'IconTools.ZipArchive') {
            $zipKey.SetValue('', '', [Microsoft.Win32.RegistryValueKind]::String)
        }
    } finally {
        $zipKey.Dispose()
    }
}

$psdKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.psd', $true)
if ($psdKey) {
    try {
        if ([string]$psdKey.GetValue('', '') -eq 'IconTools.PhotoshopDocument') {
            $psdKey.SetValue('', '', [Microsoft.Win32.RegistryValueKind]::String)
        }
    } finally {
        $psdKey.Dispose()
    }
}

Remove-CustomDefaultIcon 'Software\Classes\CLSID\{E88DCCE0-B7B3-11D1-A9F0-00AA0060FA31}\DefaultIcon'
Remove-CustomDefaultIcon 'Software\Classes\CLSID\{2c07c258-a43a-4718-b0ae-b27f8c3fe64e}\DefaultIcon'

# Restore machine-level PSD and ZIP handlers if the installer changed them.
$photoshopExe = 'D:\PHOTOSHOP\Adobe Photoshop 2020\Photoshop.exe'
if (Test-Path -LiteralPath $photoshopExe) {
    Set-DefaultValue 'SOFTWARE\Classes\Photoshop.Image.21\DefaultIcon' ($photoshopExe + ',1') -Machine
}
Set-DefaultValue 'SOFTWARE\Classes\CLSID\{2c07c258-a43a-4718-b0ae-b27f8c3fe64e}\DefaultIcon' ($photoshopExe + ',1') -Machine
Remove-CustomDefaultIcon 'SOFTWARE\Classes\CompressedFolder\DefaultIcon' -Machine
foreach ($archiveClass in @('wpmain.exe.zip', 'wpmain.exe.rar', 'wpmain.exe.7z', 'wpmain.exe.tar', 'wpmain.exe.gz', 'wpmain.exe.bz2', 'wpmain.exe.xz')) {
    Remove-CustomDefaultIcon "SOFTWARE\Classes\$archiveClass\DefaultIcon" -Machine
}

Write-Log 'Restoring desktop shortcut icons...'
$desktop = [Environment]::GetFolderPath('Desktop')
$publicDesktop = Join-Path $env:PUBLIC 'Desktop'
$shell = New-Object -ComObject WScript.Shell
$shortcutCount = 0

foreach ($folder in @($desktop, $publicDesktop)) {
    if (-not (Test-Path -LiteralPath $folder)) {
        continue
    }

    foreach ($shortcutFile in @(Get-ChildItem -LiteralPath $folder -File -Filter '*.lnk' -ErrorAction SilentlyContinue)) {
        try {
            $shortcut = $shell.CreateShortcut($shortcutFile.FullName)
            if ($shortcut.IconLocation -match 'GenericIconTemplate|IconTools') {
                Copy-Item -LiteralPath $shortcutFile.FullName -Destination $RunBackup -Force
                if (Test-Path -LiteralPath $shortcut.TargetPath) {
                    $targetExtension = [IO.Path]::GetExtension($shortcut.TargetPath).ToLowerInvariant()
                    if ($targetExtension -eq '.jar') {
                        $jarDirectory = Split-Path $shortcut.TargetPath -Parent
                        $jarIcon = Join-Path $jarDirectory 'img\icon.ico'
                        if (Test-Path -LiteralPath $jarIcon) {
                            $shortcut.IconLocation = $jarIcon + ',0'
                        } else {
                            $shortcut.IconLocation = $shortcut.TargetPath + ',0'
                        }
                    } else {
                        $shortcut.IconLocation = $shortcut.TargetPath + ',0'
                    }
                    $shortcut.Save()
                    $shortcutCount++
                }
            }
        } catch {
            Write-Log "Skipped shortcut: $($shortcutFile.FullName)"
        }
    }
}

Write-Log 'Removing desktop folder icon overrides...'
$folderCount = 0
foreach ($folder in @(Get-ChildItem -LiteralPath $desktop -Directory -Recurse -Force -ErrorAction SilentlyContinue)) {
    $desktopIni = Join-Path $folder.FullName 'desktop.ini'
    if (-not (Test-Path -LiteralPath $desktopIni)) {
        continue
    }

    try {
        $content = [IO.File]::ReadAllText($desktopIni)
        if ($content -notmatch 'GenericIconTemplate|IconTools') {
            continue
        }

        $folderItem = Get-Item -LiteralPath $folder.FullName -Force
        $folderItem.Attributes = $folderItem.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)

        $iniItem = Get-Item -LiteralPath $desktopIni -Force
        $iniItem.Attributes = [IO.FileAttributes]::Normal
        [IO.File]::Delete($desktopIni)
        $folderCount++
    } catch {
        Write-Log "Skipped desktop.ini: $desktopIni"
    }
}

Write-Log 'Refreshing Explorer...'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class RestoreIconShellRefresh {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
"@
[RestoreIconShellRefresh]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)

if (-not $NoExplorerRestart) {
    foreach ($process in @(Get-Process explorer -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $process.Id -Force
    }

    Start-Sleep -Milliseconds 800
    $cacheDirectory = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
    if (Test-Path -LiteralPath $cacheDirectory) {
        Get-ChildItem -LiteralPath $cacheDirectory -Force -File |
            Where-Object { $_.Name -match '^iconcache_.*\.db$' } |
            ForEach-Object {
                try {
                    [IO.File]::Delete($_.FullName)
                } catch {
                    # Explorer may hold a cache file briefly.
                }
            }
    }

    Start-Process -FilePath "$env:WINDIR\explorer.exe"
    Start-Sleep -Seconds 3
}

Write-Log 'Restore complete.'
Write-Log ("Shortcuts restored: {0}" -f $shortcutCount)
Write-Log ("Desktop folders restored: {0}" -f $folderCount)
Write-Log ("Log: {0}" -f $LogPath)
