# Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
# Uploads the mod to Paradox Mods with the game's own publisher, using the Paradox login the game already has.
#   First upload:   pwsh tools/publish-mod.ps1
#   Later updates:  raise ModVersion and the ChangeLog in src/FootbridgeBuilder/Properties/PublishConfiguration.xml, then
#                   pwsh tools/publish-mod.ps1 -Update
#   Page text only: pwsh tools/publish-mod.ps1 -PageOnly
#   Check it all without uploading: pwsh tools/publish-mod.ps1 -DryRun
# It builds Release, stages only the files the mod uses, pushes the source to GitHub and makes that repository public
# (GPL-3.0: the source goes out with the mod), uploads, and writes the new mod id into the page file.
param(
    [switch]$Update,
    [switch]$PageOnly,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\FootbridgeBuilder'
$page = Join-Path $project 'Properties\PublishConfiguration.xml'
$release = Join-Path $project 'bin\Release'
$repository = 'TevBenji/Footbridge-Builder'

# The publisher ships with the game, under its modding toolchain folder.
$publisher = @(
    $env:CSII_MODPUBLISHERPATH,
    'D:\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPublisher\ModPublisher.exe',
    'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPublisher\ModPublisher.exe'
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $publisher) { throw 'ModPublisher.exe not found: set CSII_MODPUBLISHERPATH to it.' }

# The upload must be built from exactly the source that gets pushed: a clean master. A dry run can start from anywhere.
if (-not $DryRun) {
    if ((git -C $root branch --show-current) -ne 'master') { throw 'Publish from master: check it out first.' }
    if (git -C $root status --porcelain) { throw 'Commit or stash your changes first: the upload must match the pushed source.' }
}

Write-Host '== Building Release'
# Without node_modules the build skips the panel bundle with only a warning, so an old one could ship with new assemblies:
# clear it first and insist the build made it again.
$bundle = 'FootbridgeBuilder.mjs', 'FootbridgeBuilder.css' | ForEach-Object { Join-Path $release $_ }
Remove-Item $bundle -ErrorAction SilentlyContinue
dotnet build (Join-Path $root 'FootbridgeBuilder.sln') -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
if ($bundle | Where-Object { -not (Test-Path $_) }) { throw 'The panel bundle was not built: run npm ci in src\FootbridgeBuilder.UI, then publish again.' }

# The test runner builds into bin\Dev; this catches it if it ever reaches Release.
if ([IO.File]::ReadAllText((Join-Path $release 'FootbridgeBuilder.dll'), [Text.Encoding]::Latin1).Contains('DevHarness')) { throw 'The Release build contains the dev harness.' }

Write-Host '== Staging the mod files'
$content = Join-Path ([IO.Path]::GetTempPath()) 'FootbridgeBuilderPublish'
Remove-Item $content -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory (Join-Path $content 'images\footbridgebuilder') | Out-Null
foreach ($file in 'FootbridgeBuilder.dll', 'FootbridgeBuilder.Core.dll', 'FootbridgeBuilder.mjs', 'FootbridgeBuilder.css', 'LICENSE', 'CREDITS.md', 'README.md') {
    Copy-Item (Join-Path $release $file) $content
}
# Only the icons the panel bundle uses: older builds leave stale hashed copies behind.
$used = Select-String -Path (Join-Path $release 'FootbridgeBuilder.mjs'), (Join-Path $release 'FootbridgeBuilder.css') `
    -Pattern 'images/footbridgebuilder/[A-Za-z0-9.-]+\.svg' -AllMatches | ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
foreach ($icon in $used) { Copy-Item (Join-Path $release $icon) (Join-Path $content $icon) }
Get-ChildItem $content -Recurse -File | ForEach-Object { '   ' + $_.FullName.Substring($content.Length + 1) }

[xml]$xml = Get-Content $page
$modId = $xml.Publish.ModId.Value
$command = if ($PageOnly) { 'Update' } elseif ($Update) { 'NewVersion' } else { 'Publish' }
if ($command -eq 'Publish' -and $modId) { throw "The page file already has mod id ${modId}: use -Update for a new version." }
if ($command -ne 'Publish' -and -not $modId) { throw 'The page file has no mod id yet: run the first upload without -Update.' }
Write-Host "== $command, version $($xml.Publish.ModVersion.Value), $($xml.Publish.AccessLevel.Value)"
if ($DryRun) { Write-Host '== Dry run: nothing pushed or uploaded.'; return }

Write-Host '== Pushing the source to GitHub'
git -C $root push origin master
if ($LASTEXITCODE -ne 0) { throw 'git push failed.' }
# GPL-3.0: the source goes out with the mod, so nothing uploads until the repository is confirmed public.
$visibility = gh repo view $repository --json visibility --jq .visibility
if ($LASTEXITCODE -ne 0) { throw 'Could not read the GitHub repository: check gh auth status.' }
if ($visibility -ne 'PUBLIC') {
    Write-Host '== Making the GitHub repository public'
    gh repo edit $repository --visibility public --accept-visibility-change-consequences
    if ($LASTEXITCODE -ne 0) { throw 'Could not make the GitHub repository public.' }
    $visibility = gh repo view $repository --json visibility --jq .visibility
    if ($LASTEXITCODE -ne 0 -or $visibility -ne 'PUBLIC') { throw "The GitHub repository is still ${visibility}: nothing was uploaded." }
}

Write-Host '== Uploading to Paradox Mods'
# The publisher targets .NET 6; a newer runtime runs it.
$env:DOTNET_ROLL_FORWARD = 'Major'
# Only the login the game has saved: the publisher takes a typed password only on its command line, where other programs
# can read it, so this script never passes one.
$arguments = @($command, $page, '-c', $content, '-v')
Push-Location $project  # the page file names its pictures relative to the project
try { & $publisher @arguments 2>&1 | Tee-Object -Variable log } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'The upload failed: the lines above say why. If it could not log in, start the game, sign in to Paradox there, and try again.' }

$found = [regex]::Match(($log -join "`n"), 'Id\s*=\s*(\d+)')
if ($command -eq 'Publish' -and $found.Success) {
    $id = $found.Groups[1].Value
    (Get-Content $page -Raw) -replace '<ModId Value="" />', "<ModId Value=`"$id`" />" | Set-Content $page -NoNewline
    git -C $root commit -q -m "Paradox Mods: mod id $id" -- $page
    if ($LASTEXITCODE -ne 0) { throw "The upload worked (mod ${id}), but committing its id failed: put $id in the page file's ModId and commit it before publishing again." }
    git -C $root push origin master
    if ($LASTEXITCODE -ne 0) { throw "The upload worked (mod ${id}), but pushing its id to GitHub failed: push master before publishing from anywhere else." }
    Write-Host "== Published as mod ${id}: https://mods.paradoxplaza.com/mods/$id/Windows"
}
Write-Host '== Done'
