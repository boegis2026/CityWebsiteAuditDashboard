@echo off
setlocal
rem Source-code launcher for a configured IIS/staging Agent. Visual Studio local mode starts its own Agent.
dotnet run --project "%~dp0CityWebsiteAuditDashboard.Agent\CityWebsiteAuditDashboard.Agent.csproj" --no-launch-profile -- %*
set "AgentExitCode=%ERRORLEVEL%"
if not "%AgentExitCode%"=="0" pause
exit /b %AgentExitCode%
