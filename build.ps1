param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$payload = Join-Path $projectRoot 'vendor\CyberpunkVRPort-0.2.3.zip'
$payloadUrl = 'https://github.com/dariulone/cyberpunk-vr-port/releases/download/0.2.3/CyberpunkVRPort-0.2.3.zip'
$payloadSha256 = '2269FA6D5F21B34AB6BB72F86A7E3A4C710D5A5C370E992EB1032A862BA4CDDC'
$output = Join-Path $projectRoot 'dist'
$publishOutput = Join-Path $projectRoot 'bin\installer-publish'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'

if (-not (Test-Path -LiteralPath $payload)) {
    Write-Host 'Downloading official Cyberpunk VR Port 0.2.3 payload...'
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

New-Item -ItemType Directory -Path $output -Force | Out-Null
$published = Join-Path $publishOutput 'CyberpunkVRPortInstaller.exe'
$versioned = Join-Path $output 'CyberpunkVRPortInstaller-0.2.3.exe'
Copy-Item -LiteralPath $published -Destination $versioned -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\7zip\License.txt') -Destination (Join-Path $output '7ZIP-LICENSE.txt') -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $versioned | Format-List


