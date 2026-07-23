using CityWebsiteAuditDashboard.ViewModels;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

public interface IAuthenticatedAuditPdfReportService
{
    /*
     * Uses the same prepared data shown on the authenticated audit
     * Details page so the PDF and dashboard report stay consistent.
     */
    byte[] CreatePdf(
        AuthenticatedAuditDetailsViewModel audit);
}
