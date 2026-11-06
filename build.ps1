param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$payload = Join-Path $projectRoot 'vendor\CyberpunkVRPort-0.1.9.zip'
$output = Join-Path $projectRoot 'dist'
$publishOutput = Join-Path $projectRoot 'bin\installer-publish'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'

if (-not (Test-Path -LiteralPath $payload)) {
    throw "Payload not found: $payload"
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


