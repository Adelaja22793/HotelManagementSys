param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = "artifacts/api"
)
$ErrorActionPreference = "Stop"
$Project = "src/HMS.Api/HMS.Api.csproj"
$Target = Join-Path $Output $Version

dotnet restore $Project
dotnet publish $Project -c Release --no-restore -p:Version=$Version -o $Target
if ($LASTEXITCODE -ne 0) { throw "API publish failed." }

# Never ship environment secrets inside the package.
Remove-Item (Join-Path $Target "appsettings.Development.json") -ErrorAction SilentlyContinue

Get-ChildItem $Target -Recurse -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetRelativePath($Target, $_.Path))" } |
    Set-Content (Join-Path $Output "$Version.sha256")
Write-Host "API $Version published to $Target"
