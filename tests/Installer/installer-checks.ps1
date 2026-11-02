param([Parameter(Mandatory = $true)][string]$Setup, [Parameter(Mandatory = $true)][string]$Source)
# Installs, updates and uninstalls the setup exe inside a throwaway folder (--root), so the real Start menu,
# Startup folder, Installed apps list, install folder and settings are never touched. Prints PASS/FAIL lines.
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('ClaudeDashboardSetupChecks-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$installDir = Join-Path $root 'Programs\Claude Dashboard'
$startMenu = Join-Path $root 'StartMenu\Claude Dashboard.lnk'
$startup = Join-Path $root 'Startup\Claude Dashboard.lnk'
$settings = Join-Path $root 'Settings'
$key = 'HKCU:\Software\ClaudeDashboard.SetupTests\Uninstall\ClaudeDashboard'
$uninstaller = Join-Path $installDir 'Uninstall Claude Dashboard.exe'
$app = Join-Path $installDir 'ClaudeDashboard.exe'
$script:failures = 0
function Check([string]$name, [scriptblock]$action) {
    try { & $action; Write-Output "PASS $name" } catch { $script:failures++; Write-Output "FAIL $name - $($_.Exception.Message)" }
}
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Run([string]$exe, [string]$arguments) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru
    Assert ($process.ExitCode -eq 0) "$([IO.Path]::GetFileName($exe)) $arguments exited with $($process.ExitCode)."
}
function Target([string]$link) { (New-Object -ComObject WScript.Shell).CreateShortcut($link).TargetPath }
function WaitGone([string]$path) {
    for ($i = 0; $i -lt 40 -and (Test-Path -LiteralPath $path); $i++) { Start-Sleep -Milliseconds 250 }
    -not (Test-Path -LiteralPath $path)
}

try {
    Check 'Setup installs the app, Start menu and Startup shortcuts, and an Installed apps entry' {
        Run $Setup "/S --root `"$root`""
        Assert ((Get-FileHash $app).Hash -eq (Get-FileHash (Join-Path $Source 'ClaudeDashboard.exe')).Hash) 'The installed exe differs from the build.'
        Assert (Test-Path -LiteralPath (Join-Path $installDir 'ClaudeDashboard.exe.config')) 'The .config is missing.'
        Assert ((Get-FileHash (Join-Path $installDir 'ClaudeHook.exe')).Hash -eq (Get-FileHash (Join-Path $Source 'ClaudeHook.exe')).Hash) 'The Claude Code bridge is missing or differs from the build.'
        Assert (Test-Path -LiteralPath $uninstaller) 'The uninstaller is missing.'
        Assert ((Target $startMenu) -eq $app) 'The Start menu shortcut does not open the installed app.'
        Assert ((Target $startup) -eq $app) 'The Startup shortcut does not open the installed app.'
        $entry = Get-ItemProperty -LiteralPath $key
        Assert ($entry.DisplayName -eq 'Claude Dashboard' -and $entry.Publisher -eq 'Ali Alyami' -and $entry.DisplayVersion) 'The Installed apps entry is incomplete.'
        Assert ($entry.UninstallString -like '*Uninstall Claude Dashboard.exe" /uninstall*' -and $entry.QuietUninstallString -like '*/S') 'The uninstall commands are wrong.'
        Assert ($entry.InstallLocation -eq $installDir -and $entry.EstimatedSize -gt 0 -and $entry.NoModify -eq 1) 'The entry details are wrong.'
    }
    Check 'An update removes the hardware sensors an older install added' {
        New-Item -ItemType Directory -Path (Join-Path $installDir 'sensors') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $installDir 'sensors\LibreHardwareMonitorLib.dll') -Value 'fixture'
        Run $Setup "/S --root `"$root`""
        Assert (-not (Test-Path -LiteralPath (Join-Path $installDir 'sensors'))) 'The old sensors folder is still there.'
        Assert ((Get-FileHash $app).Hash -eq (Get-FileHash (Join-Path $Source 'ClaudeDashboard.exe')).Hash) 'The app went missing.'
    }
    Check 'Installing again updates in place and can turn off starting with Windows' {
        Run $Setup "/S --no-startup --root `"$root`""
        Assert (-not (Test-Path -LiteralPath $startup)) 'The Startup shortcut is still there.'
        Assert ((Target $startMenu) -eq $app -and (Test-Path -LiteralPath $key)) 'The Start menu shortcut or entry went missing.'
        Run $uninstaller "/S --root `"$root`""
        Assert (Test-Path -LiteralPath $startup) 'The installed copy of setup could not install over itself.'
    }
    Check 'Uninstall removes the app, shortcuts and entry, and keeps settings' {
        New-Item -ItemType Directory -Path $settings -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $settings 'settings.xml') -Value '<Settings />'
        Run $uninstaller "/uninstall /S --root `"$root`""
        Assert (-not (Test-Path -LiteralPath $startMenu) -and -not (Test-Path -LiteralPath $startup)) 'A shortcut is left.'
        Assert (-not (Test-Path -LiteralPath $key)) 'The Installed apps entry is left.'
        Assert (WaitGone $installDir) 'The install folder is left.'
        Assert (Test-Path -LiteralPath (Join-Path $settings 'settings.xml')) 'Settings were deleted.'
    }
    Check 'Uninstall can delete settings, the Claude sign-in and the connection files too' {
        Run $Setup "/S --root `"$root`""
        Run $uninstaller "/uninstall /S --remove-settings --root `"$root`""
        Assert (WaitGone $installDir) 'The install folder is left.'
        Assert (-not (Test-Path -LiteralPath $settings)) 'Settings are left.'
    }
    Check 'Uninstalling when nothing is installed succeeds' {
        Run $Setup "/uninstall /S --root `"$root`""
    }
} finally {
    Remove-Item -LiteralPath 'HKCU:\Software\ClaudeDashboard.SetupTests' -Recurse -Force -ErrorAction SilentlyContinue
    [void](WaitGone $installDir)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
if ($script:failures -gt 0) { Write-Output "$($script:failures) setup check(s) failed."; exit 1 }
Write-Output 'All setup checks passed.'
exit 0
