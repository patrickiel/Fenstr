$ErrorActionPreference = 'Stop'

# Interactive release: prompts for bump (major/minor/patch) and notes, then
# bumps Package.appxmanifest, commits, pushes, builds (portable zip + Inno
# Setup installer), tags, and publishes the GitHub release.

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# --- Preflight ------------------------------------------------------------

gh auth status 1>$null 2>$null
if ($LASTEXITCODE -ne 0) { throw "gh CLI not authenticated. Run 'gh auth login' and retry." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php" }

$dirty = git status --porcelain
if ($dirty) { throw "Working tree is dirty. Commit or stash first:`n$dirty" }

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'master') { throw "Must be on 'master' (currently '$branch')." }

git fetch origin master --quiet
$ahead  = (git rev-list --count 'origin/master..HEAD').Trim()
$behind = (git rev-list --count 'HEAD..origin/master').Trim()
if ($ahead -ne '0' -or $behind -ne '0') {
    throw "Local master is not in sync with origin/master ($ahead ahead, $behind behind). Push or pull first."
}

# --- Read current version from Package.appxmanifest -----------------------

$manifestPath = 'Package.appxmanifest'
$manifest = Get-Content $manifestPath -Raw
if ($manifest -notmatch '<Identity\s[\s\S]*?Version="(\d+)\.(\d+)\.(\d+)\.\d+"') {
    throw "Could not find <Identity Version='x.y.z.0'> in $manifestPath"
}
$cur = [pscustomobject]@{
    Major = [int]$matches[1]
    Minor = [int]$matches[2]
    Patch = [int]$matches[3]
}
$currentVersion = "$($cur.Major).$($cur.Minor).$($cur.Patch)"

# --- Prompt: bump ---------------------------------------------------------

Write-Host ""
Write-Host "Current version: v$currentVersion" -ForegroundColor Cyan
Write-Host "Bump which part?"
Write-Host "  [1] major   ->  v$($cur.Major + 1).0.0"
Write-Host "  [2] minor   ->  v$($cur.Major).$($cur.Minor + 1).0"
Write-Host "  [3] patch   ->  v$($cur.Major).$($cur.Minor).$($cur.Patch + 1)   (default)"
$choice = Read-Host "Choice [1/2/3]"

switch ($choice) {
    '1' { $newMajor = $cur.Major + 1; $newMinor = 0;            $newPatch = 0 }
    '2' { $newMajor = $cur.Major;     $newMinor = $cur.Minor+1; $newPatch = 0 }
    default { $newMajor = $cur.Major; $newMinor = $cur.Minor;   $newPatch = $cur.Patch + 1 }
}

$Version         = "$newMajor.$newMinor.$newPatch"
$tag             = "v$Version"
$identityVersion = "$Version.0"

# --- Prompt: notes --------------------------------------------------------

Write-Host ""
Write-Host "Release notes - enter lines, blank line finishes."
Write-Host "Leave the first line blank for auto-generated notes from commits."
$noteLines = @()
while ($true) {
    $line = Read-Host
    if ([string]::IsNullOrEmpty($line)) { break }
    $noteLines += $line
}
$Notes = ($noteLines -join "`n").Trim()

# --- Bump manifest (uncommitted) ------------------------------------------

# `\b` prevents matching MinVersion / MaxVersionTested in <TargetDeviceFamily>.
# The appxmanifest only has one attribute literally named "Version".
$versionRegex = '\bVersion="\d+\.\d+\.\d+\.\d+"'
$versionRepl  = 'Version="' + $identityVersion + '"'
$newManifest  = $manifest -replace $versionRegex, $versionRepl
if ($newManifest -eq $manifest) {
    throw "Failed to rewrite Version attribute in $manifestPath."
}
# Set-Content default encoding differs between PS editions; force UTF8 with BOM to match VS-authored manifests.
Set-Content -Path $manifestPath -Value $newManifest -NoNewline -Encoding utf8

# --- Builds ---------------------------------------------------------------

$zip        = "Fenstr-$tag-win-x64.zip"
$installer  = "Fenstr-$tag-setup.exe"
$publishDir = "bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

$pfxPath = "$PSScriptRoot\Fenstr.pfx"
$pfxPassword = $null
if (Test-Path $pfxPath) {
    $pfxPassword = Read-Host "Certificate password"
} else {
    Write-Host "WARNING: $pfxPath not found; exe will not be signed." -ForegroundColor Yellow
    Write-Host "uiAccess (hooking elevated windows) requires a trusted signature." -ForegroundColor Yellow
}

dotnet restore
Write-Host "Building..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:Version=$Version `
    -p:FenstrSignPfx=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

if ($pfxPassword) {
    Write-Host "Signing..." -ForegroundColor Cyan
    $sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
    $signtool = Get-ChildItem "$sdkBin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found under $sdkBin. Install the Windows SDK." }
    & $signtool.FullName sign /fd SHA256 /f $pfxPath /p $pfxPassword "$publishDir\Fenstr.exe"
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed." }
}

Write-Host "Creating portable zip..." -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zip

Write-Host "Creating installer..." -ForegroundColor Cyan
$publishDirFull = (Resolve-Path $publishDir).Path
& $iscc "/DVersion=$Version" "/DPublishDir=$publishDirFull" "$repoRoot\installer\fenstr.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
Copy-Item "installer\Output\Fenstr-v$Version-setup.exe" $installer -Force

# --- Test -----------------------------------------------------------------

$installerFull = (Resolve-Path $installer).Path
$zipFull       = (Resolve-Path $zip).Path

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " BUILD COMPLETE - test before publishing"                                -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Installer: $installerFull"                                              -ForegroundColor Cyan
Write-Host " Zip:       $zipFull"                                                    -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "Press Enter to publish $tag to GitHub"

# --- Commit + push + tag + GitHub release ---------------------------------

Write-Host "Publishing release $tag..." -ForegroundColor Cyan
git add $manifestPath
git commit -m "chore: release $tag"
git push origin master
git tag $tag
git push origin $tag
if ($Notes) {
    gh release create $tag $zip $installer --title $tag --notes $Notes
} else {
    gh release create $tag $zip $installer --title $tag --generate-notes
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "GitHub release: https://github.com/patrickiel/Fenstr/releases/tag/$tag"
