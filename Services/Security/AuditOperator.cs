using System.Security.Claims;
using System.Security.Principal;

namespace CityWebsiteAuditDashboard.Services.Security;

// Only server authentication issues this claim. Never accept an owner from a form or hub argument.
public sealed class AuditOperator(IHttpContextAccessor accessor)
{
    public const string OwnerClaim = "urn:city-audit:operator-id";
    public const string Policy = "AuditOperator";
    public string RequireId() => RequireId(accessor.HttpContext?.User);

    public static string RequireId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(principal.FindFirstValue(OwnerClaim)))
            throw new UnauthorizedAccessException("An authorized dashboard account is required.");
        return principal.FindFirstValue(OwnerClaim)!;
    }

    public static string WindowsId(ClaimsPrincipal principal)
    {
        var sid = (principal.Identity as WindowsIdentity)?.User?.Value ??
            principal.FindFirstValue(ClaimTypes.PrimarySid);
        if (!string.IsNullOrWhiteSpace(sid)) return "windows:" + sid.ToUpperInvariant();
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) throw new UnauthorizedAccessException("Windows account name is missing.");
        return "windows-name:" + name.ToUpperInvariant();
    }

    public static ClaimsPrincipal WithOwner(ClaimsPrincipal principal, string owner)
    {
        // ClaimsPrincipal.Clone shares identities; clone them too before adding request claims.
        var copy = new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));
        foreach (var identity in copy.Identities)
            foreach (var claim in identity.FindAll(OwnerClaim).ToArray()) identity.RemoveClaim(claim);
        copy.Identities.First(x => x.IsAuthenticated).AddClaim(new Claim(OwnerClaim, owner));
        return copy;
    }
}
