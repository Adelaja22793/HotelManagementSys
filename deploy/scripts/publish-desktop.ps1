param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = "artifacts/desktop"
)
$ErrorActionPreference = "Stop"
$Project = "src/HMS.Desktop/HMS.Desktop.csproj"
$Target = Join-Path $Output $Version

dotnet restore $Project -r win-x64
dotnet build $Project -c Release --no-restore -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet publish $Project -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=false -o $Target
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Get-ChildItem $Target -Recurse -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetRelativePath($Target, $_.Path))" } |
    Set-Content (Join-Path $Output "$Version.sha256")
Write-Host "Desktop $Version published to $Target. Sign the executables before distribution."
