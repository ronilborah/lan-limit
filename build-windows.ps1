param([ValidateSet('win-x86','win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$scriptOutput = Join-Path $PSScriptRoot '.build/windows-js'
dotnet run --project Windows/CoreChecks -c Release -- $scriptOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows core checks failed.' }
node Windows/check-scripts.cjs $scriptOutput
if ($LASTEXITCODE -ne 0) { throw 'Portal JavaScript checks failed.' }
$publishDirectory = Join-Path $PSScriptRoot "dist/CampusQuota-$Runtime"
dotnet publish Windows/CampusQuota/CampusQuota.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
Copy-Item Windows/README.md (Join-Path $publishDirectory 'README.md')
$archive = Join-Path $PSScriptRoot "dist/CampusQuota-$Runtime.zip"
Compress-Archive -Path "$publishDirectory/*" -DestinationPath $archive -Force
Write-Host "Built $archive"
