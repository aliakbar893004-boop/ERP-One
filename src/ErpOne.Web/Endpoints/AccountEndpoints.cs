using Microsoft.AspNetCore.Identity;
using ErpOne.Infrastructure.Identity;

namespace ErpOne.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("/Account/Login");
        }).DisableAntiforgery();

        // GET: bisa diakses langsung dari browser address bar
        endpoints.MapGet("/Account/Logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("/Account/Login");
        }).AllowAnonymous();

        return endpoints;
    }
}
