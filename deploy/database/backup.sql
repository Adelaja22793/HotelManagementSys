/*
  HMS SQL Server backups. Schedule with SQL Agent (or Task Scheduler + sqlcmd).
  Illustrative schedule: full daily, differential every 4-6 hours, log every 15 minutes.
  Use protected destinations, unique timestamped names, and copy backups offsite.
  Set @BackupRoot before use.
*/
DECLARE @BackupRoot nvarchar(260) = N'E:\HMSBackups\';
DECLARE @Db sysname = N'HotelManagementDb';
DECLARE @Stamp nvarchar(20) = FORMAT(SYSUTCDATETIME(), 'yyyyMMdd_HHmmss');
DECLARE @Kind nvarchar(10) = N'$(Kind)'; -- FULL | DIFF | LOG (pass with sqlcmd -v Kind=FULL)
DECLARE @File nvarchar(400) = @BackupRoot + @Db + N'_' + @Kind + N'_' + @Stamp +
    CASE WHEN @Kind = N'LOG' THEN N'.trn' ELSE N'.bak' END;

-- One-time: point-in-time recovery requires the FULL recovery model, then a new full backup.
-- ALTER DATABASE [HotelManagementDb] SET RECOVERY FULL;

IF @Kind = N'FULL'
    BACKUP DATABASE @Db TO DISK = @File WITH CHECKSUM, COMPRESSION, INIT;
ELSE IF @Kind = N'DIFF'
    BACKUP DATABASE @Db TO DISK = @File WITH DIFFERENTIAL, CHECKSUM, COMPRESSION, INIT;
ELSE IF @Kind = N'LOG'
    BACKUP LOG @Db TO DISK = @File WITH CHECKSUM, COMPRESSION, INIT;
ELSE
    THROW 50000, 'Kind must be FULL, DIFF or LOG.', 1;

-- Verification is useful but does NOT replace periodic real restore tests.
RESTORE VERIFYONLY FROM DISK = @File WITH CHECKSUM;
