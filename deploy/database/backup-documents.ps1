<#
.SYNOPSIS
    Copies the private document store to a new timestamped folder and writes a SHA-256 catalogue of every file.
    Schedule it hourly (the documents RPO), after the database log backup, and copy the result offsite.
    Older folders are kept; prune them with your retention policy, never by overwriting.
#>
param(
    [Parameter(Mandatory)][string]$StoragePath,     # Documents__StoragePath of the API
    [Parameter(Mandatory)][string]$BackupRoot       # e.g. E:\HMSBackups\documents
)
$ErrorActionPreference = "Stop"
$target = Join-Path $BackupRoot ("documents-{0:yyyyMMdd-HHmmss}" -f (Get-Date).ToUniversalTime())
# /E copies subfolders; /COPY:DAT keeps data, attributes and times; /R and /W keep a locked file from hanging the job.
robocopy $StoragePath $target /E /COPY:DAT /R:2 /W:5 /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Get-ChildItem $target -Recurse -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($target, $_.Path))" } |
    Set-Content (Join-Path $target "catalogue.sha256")
Write-Host "Documents copied to $target"
