param([string]$Output = "artifacts/database/deploy.sql")
$ErrorActionPreference = "Stop"
dotnet tool restore
dotnet ef migrations script --idempotent `
    --project src/HMS.Infrastructure/HMS.Infrastructure.csproj `
    --startup-project src/HMS.Infrastructure/HMS.Infrastructure.csproj `
    --output $Output
if ($LASTEXITCODE -ne 0) { throw "Migration script generation failed." }
Write-Host "Review $Output, rehearse on staging, take a full backup, then apply in production."
