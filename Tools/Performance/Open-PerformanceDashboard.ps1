$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dashboardProject = Join-Path $projectRoot "Tools\PerformanceDashboard\PerformanceDashboard.csproj"

dotnet run --project $dashboardProject --configuration Release
exit $LASTEXITCODE
