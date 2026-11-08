param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish/windows/$Runtime"
$stage = Join-Path $root ('publish/windows/.build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
function Check-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE" }
}
try {
    Push-Location (Join-Path $root 'web')
    try {
        & npm.cmd ci --no-audit --no-fund
        Check-Exit 'npm ci'
        & npx.cmd ng build
        Check-Exit 'Angular build'
    } finally { Pop-Location }
    & dotnet publish (Join-Path $root 'src/RabbitEars.Api') -c Release -r $Runtime --self-contained true -o "$stage/app"
    Check-Exit 'API publish'
    & dotnet publish (Join-Path $root 'src/RabbitEars.Desktop') -c Release -r $Runtime --self-contained true -o "$stage/launcher"
    Check-Exit 'Launcher publish'
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $keys = [IO.File]::ReadAllText("$stage/app/mpv/input.conf") + "`n" + [IO.File]::ReadAllText("$root/desktop/input-extra.conf")
    [IO.File]::WriteAllText("$stage/app/mpv/input-desktop.conf", $keys, $utf8)
    Copy-Item "$root/desktop/Rabbit-Ears.ps1" $stage
    Copy-Item "$root/desktop/desktop.windows.example.json" "$stage/desktop.example.json"
    Copy-Item "$root/docs/windows-setup.md" "$stage/README.md"
    Copy-Item "$root/LICENSE", "$root/THIRD-PARTY-NOTICES.md" $stage
    $revision = & git -C $root rev-parse HEAD
    Check-Exit 'Git revision'
    @{ runtime = $Runtime; commit = $revision; sdk = (& dotnet --version) } |
        ConvertTo-Json | Set-Content "$stage/build.json" -Encoding UTF8
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Move-Item $stage $out
    Write-Host "Built $out. Start Rabbit-Ears.ps1 with mpv and ffprobe installed."
} finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
