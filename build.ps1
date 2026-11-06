param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$payload = Join-Path $projectRoot 'vendor\CyberpunkVRPort-0.2.2.zip'
$payloadUrl = 'https://github.com/dariulone/cyberpunk-vr-port/releases/download/0.2.2/CyberpunkVRPort-0.2.2.zip'
$payloadSha256 = '353B4E41FBB0A5A2D62524230F25D96F22C24FC006CB6D6B93D26926B6951D73'
$output = Join-Path $projectRoot 'dist'
$publishOutput = Join-Path $projectRoot 'bin\installer-publish'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'

if (-not (Test-Path -LiteralPath $payload)) {
    Write-Host 'Downloading official Cyberpunk VR Port 0.2.2 payload...'
    $temporaryPayload = "$payload.download"
    Invoke-WebRequest -Uri $payloadUrl -OutFile $temporaryPayload -Headers @{ 'User-Agent' = 'CyberpunkVRPortInstaller-build' }
    Move-Item -LiteralPath $temporaryPayload -Destination $payload -Force
}

$actualPayloadSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash
if ($actualPayloadSha256 -ne $payloadSha256) {
    throw "Payload checksum mismatch. Expected $payloadSha256, got $actualPayloadSha256"
}

dotnet publish (Join-Path $projectRoot 'CyberpunkVRInstaller.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishOutput

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$published = Join-Path $publishOutput 'CyberpunkVRPortInstaller.exe'
$versioned = Join-Path $output 'CyberpunkVRPortInstaller-0.1.9.exe'
Copy-Item -LiteralPath $published -Destination $versioned -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\7zip\License.txt') -Destination (Join-Path $output '7ZIP-LICENSE.txt') -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $versioned | Format-List


