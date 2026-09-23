using IdentityFoundation.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Keycloak:Authority is not configured.");
var requireHttps = builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakAuthority;
        options.RequireHttpsMetadata = requireHttps; // false only for local dev over http
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "name";
        // NOTE (documented simplification): by default Keycloak does not put the
        // client id in the "aud" claim unless an audience mapper is added to the
        // client scope. For this demo we disable audience validation and instead
        // rely on Authority (issuer) + signature validation, which is already
        // enough to prove the token was issued by *our* Keycloak realm.
        // A production setup would add a dedicated audience mapper and re-enable
        // ValidateAudience with the expected client id.
    });

builder.Services.AddSingleton<IClaimsTransformation, KeycloakRoleClaimsTransformation>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ClaimsPrincipalAccessor>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireUserRole", policy => policy.RequireRole("user"))
    .AddPolicy("RequireAdminRole", policy => policy.RequireRole("admin"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/public", () => Results.Ok(new { message = "No token required." }));

app.MapGet("/me", (ClaimsPrincipalAccessor accessor) => Results.Ok(accessor.Describe()))
    .RequireAuthorization();

app.MapGet("/user", () => Results.Ok(new { message = "Hello, authenticated user." }))
    .RequireAuthorization("RequireUserRole");

app.MapGet("/admin", () => Results.Ok(new { message = "Hello, admin. This is a protected resource." }))
    .RequireAuthorization("RequireAdminRole");

app.Run();

// Small helper so /me can show exactly what claims came through, useful while
// demoing the token flow and debugging role mapping.
public class ClaimsPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
{
    public object Describe()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return new
        {
            name = user?.Identity?.Name,
            isAuthenticated = user?.Identity?.IsAuthenticated ?? false,
            roles = user?.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value),
            claims = user?.Claims.Select(c => new { c.Type, c.Value })
        };
    }
}
