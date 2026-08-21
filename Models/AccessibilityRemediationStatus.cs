namespace CityWebsiteAuditDashboard.Models;

/// <summary>
/// Represents the current lifecycle state of an accessibility remediation item.
/// 
/// Fixed means the issue has been reported as addressed, but it has not yet
/// been confirmed by a successful accessibility retest.
/// Verified means the remediation has been retested and confirmed.
/// </summary>
public enum AccessibilityRemediationStatus
{
    Open = 0,

    InProgress = 1,

    Fixed = 2,

    Verified = 3,

    WontFix = 4
}
