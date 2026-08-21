namespace CityWebsiteAuditDashboard.Models;

/// <summary>
/// Result of retesting a tracked accessibility remediation item.
/// 
/// NotDetected means the tracked issue was not found during this retest.
/// It does not automatically mean Verified; verification remains a
/// separate explicit workflow step.
/// </summary>
public enum AccessibilityRemediationRetestResult
{
    Detected = 0,
    NotDetected = 1,
    Inconclusive = 2,
    Failed = 3
}
