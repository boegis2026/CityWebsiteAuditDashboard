USE [CityWebsiteAuditDashboardDb];
GO
IF DATABASE_PRINCIPAL_ID(N'CityAuditReportingReader') IS NULL
    THROW 51000, 'Run Grant-ReportingReadOnly first to create the reporting role.', 1;
GRANT SELECT ON OBJECT::dbo.AuthenticatedAuditFindingNodes TO CityAuditReportingReader;
PRINT N'Technical element read access enabled. No records changed.';
