# Run from PowerShell on a developer PC with the .NET 10 SDK.
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'CityWebsiteAuditDashboard.Agent\CityWebsiteAuditDashboard.Agent.csproj'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $root) ('CityAuditAgentPackage-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output folder so the package contains only the new publish.' }
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Agent publishing failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-AuditAgent.ps1') -Destination $OutputDirectory
Write-Host "Agent package ready: $OutputDirectory"
Write-Host 'Distribute the entire folder through your approved file-sharing method. Users run Install-AuditAgent.ps1 once.'
