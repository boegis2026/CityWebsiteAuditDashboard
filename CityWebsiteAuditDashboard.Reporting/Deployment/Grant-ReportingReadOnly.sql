-- DBA: run against the existing application database on the intended SQL server.
-- First create a dedicated Windows server login, using your actual app-pool identity.
-- Local IIS + local SQL Express example (run in master after creating the app pool):
-- CREATE LOGIN [IIS APPPOOL\CityAuditReports] FROM WINDOWS;
-- Remote SQL example: use a dedicated approved domain service account or gMSA.
-- Do not use the auditing application's existing write-enabled login.
USE [CityWebsiteAuditDashboardDb];
GO
SET XACT_ABORT ON;
DECLARE @LoginName sysname = N'REPLACE_WITH_REPORTING_LOGIN';
DECLARE @RoleName sysname = N'CityAuditReportingReader';
DECLARE @Sql nvarchar(max);
IF @LoginName = N'REPLACE_WITH_REPORTING_LOGIN'
    THROW 51000, 'Set @LoginName to the dedicated reporting SQL server login.', 1;
IF SUSER_ID(@LoginName) IS NULL
    THROW 51001, 'The SQL server login must be created by the DBA first.', 1;
IF IS_SRVROLEMEMBER(N'sysadmin', @LoginName) = 1
    THROW 51002, 'The reporting login must not be a sysadmin.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF DATABASE_PRINCIPAL_ID(@RoleName) IS NULL
        EXEC(N'CREATE ROLE [CityAuditReportingReader] AUTHORIZATION [dbo];');
    IF DATABASE_PRINCIPAL_ID(@LoginName) IS NULL
    BEGIN
        SET @Sql = N'CREATE USER ' + QUOTENAME(@LoginName)
            + N' FOR LOGIN ' + QUOTENAME(@LoginName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;
    IF EXISTS (
        SELECT 1 FROM sys.database_principals
        WHERE name = @LoginName AND sid <> SUSER_SID(@LoginName)
    ) THROW 51003, 'The existing database user maps to a different login. Ask the DBA to review.', 1;
    IF IS_ROLEMEMBER(N'db_owner', @LoginName) = 1 OR
       IS_ROLEMEMBER(N'db_securityadmin', @LoginName) = 1 OR
       IS_ROLEMEMBER(N'db_ddladmin', @LoginName) = 1 OR
       IS_ROLEMEMBER(N'db_datawriter', @LoginName) = 1
        THROW 51004, 'Use a dedicated login without database administration or writer roles.', 1;

    GRANT SELECT ON OBJECT::dbo.AuthenticatedAuditRuns TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AuthenticatedAuditSteps TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AuthenticatedAuditFindings TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AuthenticatedAuditFindingNodes TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AccessibilityRemediationItems TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AccessibilityRemediationFindingOccurrences TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AccessibilityRemediationHistories TO CityAuditReportingReader;
    GRANT SELECT ON OBJECT::dbo.AccessibilityRemediationRetests TO CityAuditReportingReader;
    DENY INSERT, UPDATE, DELETE, EXECUTE TO CityAuditReportingReader;
    IF IS_ROLEMEMBER(@RoleName, @LoginName) <> 1
    BEGIN
        SET @Sql = N'ALTER ROLE [CityAuditReportingReader] ADD MEMBER '
            + QUOTENAME(@LoginName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
-- The DBA must also check inherited Windows-group permissions and ensure this
-- identity does not own the database, have CONTROL, or belong to privileged roles.
PRINT N'Reporting SELECT permissions configured. No application records or schema were changed.';
