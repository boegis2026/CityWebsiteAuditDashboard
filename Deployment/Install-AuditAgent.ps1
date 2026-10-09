# Run this from the published Agent package as the Windows user who will audit.
[CmdletBinding()]
param([string]$DashboardUrl, [switch]$NoStartup)
$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'CityWebsiteAuditDashboard.Agent.exe'
if (!(Test-Path -LiteralPath $sourceExe)) { throw 'Run this installer from the published workstation Agent package.' }
$settingsPath = Join-Path $env:LOCALAPPDATA 'CityWebsiteAuditDashboard\Agent\settings.json'
if (!$DashboardUrl -and (Test-Path -LiteralPath $settingsPath)) {
    $DashboardUrl = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).DashboardUrl
}
if (!$DashboardUrl) { $DashboardUrl = Read-Host 'Enter the staging dashboard HTTPS address (including its application path)' }
$uri = $null
if (![Uri]::TryCreate($DashboardUrl, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
    throw 'A valid HTTPS dashboard base address is required.'
}
$installDirectory = Join-Path $env:LOCALAPPDATA 'CityWebsiteAuditDashboard\AgentApp'
$targetExe = Join-Path $installDirectory 'CityWebsiteAuditDashboard.Agent.exe'
foreach ($process in [Diagnostics.Process]::GetProcessesByName('CityWebsiteAuditDashboard.Agent')) {
    $executable = $null
    try { $executable = $process.MainModule.FileName } catch { }
    if ($executable -eq $targetExe) { throw 'Close your installed Agent window with Ctrl+C before updating it.' }
}
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ne [IO.Path]::GetFullPath($installDirectory).TrimEnd('\')) {
    Get-ChildItem -LiteralPath $PSScriptRoot -Force | Copy-Item -Destination $installDirectory -Recurse -Force
}
& $targetExe --configure $uri.AbsoluteUri
if ($LASTEXITCODE -ne 0) { throw 'Could not save the Agent dashboard address.' }
$shell = New-Object -ComObject WScript.Shell
$folders = @([Environment]::GetFolderPath('DesktopDirectory'))
if (!$NoStartup) { $folders += [Environment]::GetFolderPath('Startup') }
foreach ($folder in $folders) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'City Website Audit Agent.lnk'))
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = 'Connect the audit browser using your Windows account'
    $shortcut.Save()
}
Write-Host 'Installed for your Windows account. No pairing key or administrator password is needed.'
if (!$NoStartup) { Write-Host 'The Agent will also start at your next Windows sign-in.' }
Start-Process -FilePath $targetExe -WorkingDirectory $installDirectory
