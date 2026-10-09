# Run in an elevated PowerShell only when configuring a new local IIS deployment.
[CmdletBinding()]
param(
    [string]$PublishedDirectory = 'C:\inetpub\CityWebsiteAuditDashboard',
    [string]$PoolName = 'CityWebsiteAuditDashboardPool',
    [string[]]$AllowedOperators
)
$ErrorActionPreference = 'Stop'
$settingsDirectory = Join-Path $env:ProgramData 'CityWebsiteAuditDashboard'
$settingsPath = Join-Path $settingsDirectory 'staging.settings.json'
if (Test-Path -LiteralPath $settingsPath) {
    Write-Host "Machine settings already exist at $settingsPath. The existing file was kept."
    Write-Host 'Windows Authentication now replaces pairing keys. Verify AllowedOperators in the existing file.'
    return
}
$appSettingsPath = Join-Path $PublishedDirectory 'appsettings.json'
$publishedSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
$connectionString = $publishedSettings.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'DefaultConnection was not found in the deployed appsettings.json.'
}
if ($connectionString -match '(?i)\(localdb\)') {
    throw 'Configure DefaultConnection for the IIS SQL Server database before creating machine settings; LocalDB is a development connection.'
}
if (!$AllowedOperators -or $AllowedOperators.Count -eq 0) {
    $AllowedOperators = @([Security.Principal.WindowsIdentity]::GetCurrent().Name)
}
foreach ($account in $AllowedOperators) {
    if ([string]::IsNullOrWhiteSpace($account) -or $account -notmatch '^[^\\]+\\[^\\]+$') {
        throw 'Each allowed operator must be a Windows account in DOMAIN\username format.'
    }
}
New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
& icacls.exe $settingsDirectory /inheritance:r /grant:r `
    '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "IIS AppPool\${PoolName}:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not configure settings-folder permissions. Settings were not written.'
}
$settings = [ordered]@{
    ConnectionStrings = @{ DefaultConnection = $connectionString }
    AuditAgent = @{ LocalDevelopment = $false }
    Staging = @{ AllowedOperators = @($AllowedOperators) }
}
$json = $settings | ConvertTo-Json -Depth 5
# CreateNew also prevents overwriting a file created since the initial check.
$stream = [IO.File]::Open($settingsPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $stream.Write($bytes, 0, $bytes.Length)
}
finally { $stream.Dispose() }
Write-Host "SUCCESS: Saved machine settings to $settingsPath"
Write-Host 'No pairing key is used. Keep machine settings outside Git and the publish folder.'
Write-Host 'Enable Windows Authentication, disable Anonymous Authentication, and restart the IIS application pool.'
