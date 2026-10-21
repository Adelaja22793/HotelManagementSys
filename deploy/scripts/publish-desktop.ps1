param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = "artifacts/desktop",
    # Optional: produce a signed update manifest for workstations that already have the app.
    [string]$PackageUrl,          # e.g. https://updates.hotel.local/hms/1.2.0.zip
    [string]$SigningKey,          # private PEM from 'HMS.ReleaseTool keygen'; keep it in the release secret store
    [string]$KeyId
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

# The built-in server (Api:Mode "Local"): the desktop app starts server\HMS.Api.exe itself on 127.0.0.1.
dotnet publish src/HMS.Api/HMS.Api.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o (Join-Path $Target "server")
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }
Remove-Item (Join-Path $Target "server/appsettings.Development.json") -ErrorAction SilentlyContinue

# The launcher (HMS.exe) sits in the install root and starts versions\<current>\HMS.Desktop.exe.
dotnet publish src/HMS.Launcher/HMS.Launcher.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true -o (Join-Path $Output "launcher")
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

Get-ChildItem $Target -Recurse -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetRelativePath($Target, $_.Path))" } |
    Set-Content (Join-Path $Output "$Version.sha256")

# Sign HMS.Desktop.exe, HMS.exe and the HMS*.dll files with the publisher certificate (signtool) before this point
# in a real release; the update package must contain the signed files.
$Zip = Join-Path $Output "$Version.zip"
if (Test-Path $Zip) { Remove-Item $Zip }
Compress-Archive -Path (Join-Path $Target "*") -DestinationPath $Zip

if ($SigningKey) {
    if (-not $PackageUrl -or -not $KeyId) { throw "-PackageUrl and -KeyId are required with -SigningKey." }
    dotnet run --project tools/HMS.ReleaseTool -c Release -- sign --package $Zip --version $Version --url $PackageUrl `
        --key $SigningKey --key-id $KeyId --out (Join-Path $Output "manifest.json")
    if ($LASTEXITCODE -ne 0) { throw "Manifest signing failed." }
}
Write-Host "Desktop $Version published to $Target and packaged as $Zip."
