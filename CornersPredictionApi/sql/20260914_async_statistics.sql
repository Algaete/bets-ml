-- Operational tuning, applied explicitly by a database administrator.
-- Not part of application startup: runtime accounts need not have ALTER DATABASE.
-- Keep automatic statistics enabled, but do not make interactive queries wait
-- for a large audit-table statistics refresh before compiling their plan.
-- https://learn.microsoft.com/en-us/sql/relational-databases/statistics/statistics
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM sys.databases
    WHERE database_id=DB_ID() AND is_auto_update_stats_async_on=0)
BEGIN
    DECLARE @Sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME())
        + N' SET AUTO_UPDATE_STATISTICS_ASYNC ON;';
    EXEC sys.sp_executesql @Sql;
END;

-- Azure SQL / SQL Server 2022+: let foreground compilation pass ahead of the
-- background request while it waits to publish updated statistics metadata.
IF EXISTS (SELECT 1 FROM sys.database_scoped_configurations
    WHERE name=N'ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY' AND value=0)
    EXEC(N'ALTER DATABASE SCOPED CONFIGURATION SET ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY = ON;');

SELECT name, is_auto_update_stats_on, is_auto_update_stats_async_on,
    AsyncLowPriority = (SELECT value FROM sys.database_scoped_configurations
        WHERE name=N'ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY')
FROM sys.databases WHERE database_id=DB_ID();

-- Reversal for the original 2026-09-14 settings (OFF / 0):
-- ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS_ASYNC OFF;
-- ALTER DATABASE SCOPED CONFIGURATION SET ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY = OFF;
