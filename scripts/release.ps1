param([string]$Version, [switch]$UpdateShortcut, [switch]$Force)
# Builds a tagged version from a clean checkout of its tag, runs every check, and copies
# the app into releases\<version>. Defaults to the newest vX.Y.Z tag reachable from main.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not $Version) { $Version = git describe --tags --abbrev=0 --match 'v[0-9]*' main }
    if (-not $Version) { throw 'No vX.Y.Z tag found on main.' }
    git rev-parse --verify --quiet "refs/tags/$Version" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Tag $Version does not exist." }
    $commit = git rev-parse --short "$Version^{commit}"

    $releasePath = Join-Path $projectRoot "releases\$Version"
    if (Test-Path -LiteralPath $releasePath) {
        if (-not $Force) { throw "releases\$Version already exists. Use -Force to rebuild it (close the app first)." }
        Remove-Item -LiteralPath $releasePath -Recurse -Force
    }

    # Build from the tag itself so uncommitted or later work never leaks into a release.
    $buildCheckout = Join-Path $projectRoot ".worktrees\release-$Version"
    if (Test-Path -LiteralPath $buildCheckout) { git worktree remove --force $buildCheckout }
    git worktree add --detach $buildCheckout $Version
    if ($LASTEXITCODE -ne 0) { throw "Could not check out $Version." }
    try {
        & (Join-Path $buildCheckout 'scripts\build.ps1') -Test
        New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
        Copy-Item -Path (Join-Path $buildCheckout 'artifacts\app\*') -Destination $releasePath -Recurse
        # The setup exe (tags from v0.12.0 on) installs this release with Start menu and startup shortcuts.
        $packageScript = Join-Path $buildCheckout 'scripts\package.ps1'
        if (Test-Path -LiteralPath $packageScript) { & $packageScript -SourceDirectory $releasePath -OutputDirectory $releasePath -Version $Version -Test }
    } finally {
        git worktree remove --force $buildCheckout
        git worktree prune
    }
} finally { Pop-Location }

@(
    "Claude Dashboard $Version"
    "Commit: $commit"
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    'Settings: %LOCALAPPDATA%\WidgetScreen\settings.xml (shared with every build)'
    $(if (Test-Path -LiteralPath (Join-Path $releasePath "ClaudeDashboard-Setup-$Version.exe")) { "Setup: ClaudeDashboard-Setup-$Version.exe installs it for you, with Start menu and startup shortcuts and an uninstaller" })
) | Set-Content -LiteralPath (Join-Path $releasePath 'release.txt') -Encoding utf8
Write-Output "Release: $releasePath\ClaudeDashboard.exe"

if ($UpdateShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    $shortcutPath = Join-Path $desktop 'Claude Dashboard.lnk'
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $releasePath 'ClaudeDashboard.exe'
    $shortcut.WorkingDirectory = $releasePath
    $shortcut.Description = "Claude Dashboard $Version"
    $shortcut.Save()
    Write-Output "Shortcut: $shortcutPath -> $($shortcut.TargetPath)"
    # The app was called Widget Screen until v0.42 (decision 026): its desktop shortcut goes when it opens one of this
    # folder's own releases, so the desktop has one shortcut to the newest version. Others are left alone.
    $oldShortcutPath = Join-Path $desktop 'Widget Screen.lnk'
    if (Test-Path -LiteralPath $oldShortcutPath) {
        $oldTarget = $shell.CreateShortcut($oldShortcutPath).TargetPath
        $releasesRoot = (Join-Path $projectRoot 'releases').TrimEnd('\') + '\'
        if ($oldTarget -and $oldTarget.StartsWith($releasesRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $oldShortcutPath -Force
            Write-Output "Removed the old Widget Screen shortcut ($oldTarget)"
        }
    }
}
