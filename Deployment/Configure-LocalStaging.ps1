$ErrorActionPreference = 'Stop'

$settingsDirectory = Join-Path $env:ProgramData 'CityWebsiteAuditDashboard'
$settingsPath = Join-Path $settingsDirectory 'staging.settings.json'
$publishedDirectory = 'C:\inetpub\CityWebsiteAuditDashboard'
$poolName = 'CityWebsiteAuditDashboardPool'

if (Test-Path -LiteralPath $settingsPath) {
    throw "Machine settings already exist at $settingsPath. Stop here and keep the existing file."
}

# Read the existing key without displaying it.
$key = $env:AuditAgent__SharedKey

if ([string]::IsNullOrWhiteSpace($key)) {
    $webConfigPath = Join-Path $publishedDirectory 'web.config'

    [xml]$publishedWebConfig =
        Get-Content -LiteralPath $webConfigPath -Raw

    $entry = $publishedWebConfig.SelectSingleNode(
        "//aspNetCore/environmentVariables/environmentVariable[@name='AuditAgent__SharedKey']"
    )

    if ($null -ne $entry) {
        $key = $entry.GetAttribute('value')
    }
}

if ([string]::IsNullOrWhiteSpace($key) -or
    $key.Length -lt 32 -or
    $key.Length -gt 1024) {
    throw 'A valid existing pairing key was not found. Stop here; keep the IIS key unchanged.'
}

$appSettingsPath =
    Join-Path $publishedDirectory 'appsettings.json'

$publishedSettings =
    Get-Content -LiteralPath $appSettingsPath -Raw |
    ConvertFrom-Json

$connectionString =
    $publishedSettings.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'DefaultConnection was not found in the deployed appsettings.json.'
}

New-Item -ItemType Directory `
    -Path $settingsDirectory `
    -Force | Out-Null

# Restrict access before saving the key.
& icacls.exe $settingsDirectory `
    /inheritance:r `
    /grant:r `
    '*S-1-5-18:(OI)(CI)F' `
    '*S-1-5-32-544:(OI)(CI)F' `
    "IIS AppPool\${poolName}:(OI)(CI)RX" | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw 'Could not configure settings-folder permissions. Settings were not written.'
}

$windowsAccount =
    [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$settings = [ordered]@{
    ConnectionStrings = @{
        DefaultConnection = $connectionString
    }
    AuditAgent = @{
        SharedKey = $key
    }
    Staging = @{
        AllowedOperators = @($windowsAccount)
    }
}

$settings |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $settingsPath -Encoding UTF8

Write-Host "SUCCESS: Saved machine settings to $settingsPath"
Write-Host 'The pairing key was not displayed.'
Write-Host 'Keep this settings file outside Git and the publish folder.'