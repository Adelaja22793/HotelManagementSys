<#
.SYNOPSIS
    Restore rehearsal: restores a backup chain into an isolated database, copies a document backup next to it,
    and runs the API's restore check. Run it on a non-production server at least monthly and keep the report.

.EXAMPLE
    ./restore-test.ps1 -SqlServer sql-test01 -FullBackup E:\HMSBackups\HotelManagementDb_FULL_20261020.bak `
        -LogBackups (Get-ChildItem E:\HMSBackups\*_LOG_20261020*.trn | Sort-Object Name) `
        -StopAt "2026-10-20T18:30:00" -DocumentBackup E:\HMSBackups\documents-20261020 `
        -ApiFolder D:\HMS\api\1.4.0

.NOTES
    Needs the SqlServer PowerShell module (Install-Module SqlServer) and a published API folder of the same release.
    The restored database name ends in _RestoreTest; it is dropped at the end unless -Keep is given.
#>
param(
    [Parameter(Mandatory)][string]$SqlServer,
    [Parameter(Mandatory)][string]$FullBackup,
    [string]$DiffBackup,
    [string[]]$LogBackups = @(),
    [string]$StopAt,
    [Parameter(Mandatory)][string]$DocumentBackup,
    [Parameter(Mandatory)][string]$ApiFolder,
    [string]$Database = "HotelManagementDb_RestoreTest",
    [string]$WorkFolder = (Join-Path $env:TEMP "hms-restore-test"),
    [string]$ReportFolder = ".",
    [switch]$Keep
)
$ErrorActionPreference = "Stop"
Import-Module SqlServer
if ($Database -notmatch "_RestoreTest$") { throw "Use a database name ending in _RestoreTest so production is never overwritten." }

$started = Get-Date
function Invoke-Sql([string]$Query) {
    Invoke-Sqlcmd -ServerInstance $SqlServer -Database master -Query $Query -QueryTimeout 0 -TrustServerCertificate
}
function Quote([string]$Text) { $Text.Replace("'", "''") }

# 1. Every file must pass its checksum before anything is restored.
foreach ($file in @($FullBackup, $DiffBackup) + $LogBackups | Where-Object { $_ }) {
    Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$(Quote $file)' WITH CHECKSUM"
}

# 2. Restore the chain under the test name, moving the files beside the server's default data folder.
$dataPath = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS P").P
$moves = Invoke-Sql "RESTORE FILELISTONLY FROM DISK = N'$(Quote $FullBackup)'" | ForEach-Object -Begin { $i = 0 } -Process {
    $ext = if ($_.Type -eq "L") { "ldf" } else { "mdf" }
    "MOVE N'$(Quote $_.LogicalName)' TO N'$(Quote (Join-Path $dataPath "$Database`_$i.$ext"))'"
    $i++
}
Invoke-Sql "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END"
$chain = @($DiffBackup) + $LogBackups | Where-Object { $_ }
$recovery = if ($chain.Count -eq 0) { "RECOVERY" } else { "NORECOVERY" }
Invoke-Sql "RESTORE DATABASE [$Database] FROM DISK = N'$(Quote $FullBackup)' WITH $($moves -join ', '), CHECKSUM, $recovery"
if ($DiffBackup) {
    $recovery = if ($LogBackups.Count -eq 0) { "RECOVERY" } else { "NORECOVERY" }
    Invoke-Sql "RESTORE DATABASE [$Database] FROM DISK = N'$(Quote $DiffBackup)' WITH CHECKSUM, $recovery"
}
for ($n = 0; $n -lt $LogBackups.Count; $n++) {
    $last = $n -eq $LogBackups.Count - 1
    $options = "CHECKSUM, " + $(if ($last) { "RECOVERY" } else { "NORECOVERY" })
    if ($StopAt) { $options += ", STOPAT = '$StopAt'" }
    Invoke-Sql "RESTORE LOG [$Database] FROM DISK = N'$(Quote $LogBackups[$n])' WITH $options"
}
Invoke-Sql "DBCC CHECKDB (N'$Database') WITH NO_INFOMSGS, ALL_ERRORMSGS"

# 3. Copy the document backup into a private work folder (never the production document path).
$documents = Join-Path $WorkFolder "documents"
if (Test-Path $WorkFolder) { Remove-Item $WorkFolder -Recurse -Force }
Copy-Item $DocumentBackup $documents -Recurse

# 4. Run the API's own check against the copy: files present and unaltered, books balanced, schema current.
$env:ConnectionStrings__HotelManagementDb = "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=True"
$env:Database__Provider = "SqlServer"
$env:Documents__StoragePath = $documents
$env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)) # unused, but required at startup
$output = & dotnet (Join-Path $ApiFolder "HMS.Api.dll") verify-restore
$exit = $LASTEXITCODE
Remove-Item Env:ConnectionStrings__HotelManagementDb, Env:Documents__StoragePath, Env:Jwt__SigningKey

$report = Join-Path $ReportFolder ("restore-test-{0:yyyyMMdd-HHmm}.json" -f $started)
$output | Set-Content $report
Write-Host ($output -join "`n")
Write-Host ("Restore rehearsal took {0:n0} minutes. Report: {1}" -f ((Get-Date) - $started).TotalMinutes, $report)

if (-not $Keep) {
    Invoke-Sql "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];"
    Remove-Item $WorkFolder -Recurse -Force
}
if ($exit -ne 0) { throw "Restore check found problems (exit code $exit). See $report." }
