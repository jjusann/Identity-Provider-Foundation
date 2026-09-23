using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace IdentityFoundation.Api;

/// <summary>
/// Keycloak puts realm roles inside a "realm_access" claim shaped like:
///   "realm_access": { "roles": ["user", "admin"] }
/// ASP.NET Core's [Authorize(Roles = "...")] only understands flat
/// ClaimTypes.Role claims, so this transformation unpacks realm_access
/// and re-emits each role as a standard role claim, once per request,
/// after the JWT has already been validated.
/// </summary>
public class KeycloakRoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        // Avoid adding roles twice if this runs more than once for the same principal.
        if (identity.HasClaim(c => c.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal);
        }

        var realmAccessClaim = identity.FindFirst("realm_access");
        if (realmAccessClaim is null)
        {
            return Task.FromResult(principal);
        }

        using var doc = JsonDocument.Parse(realmAccessClaim.Value);
        if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
        {
            foreach (var role in rolesElement.EnumerateArray())
            {
                var roleName = role.GetString();
                if (!string.IsNullOrWhiteSpace(roleName))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                }
            }
        }

        return Task.FromResult(principal);
    }
}
