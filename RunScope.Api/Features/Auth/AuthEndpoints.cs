using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using RunScope.Core.Data;
using RunScope.Core.Models;
using System.Security.Claims;

namespace RunScope.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // GET /api/auth/me — update to use DB user ID from claim
        group.MapGet("/me", async (HttpContext ctx, RunScopeDbContext db) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.Unauthorized();

            return Results.Ok(new
            {
                id = user.Id,
                email = user.Email,
                name = user.Name,
                avatarUrl = user.AvatarUrl,
            });
        }).RequireAuthorization();

        // GET /api/auth/google — initiates OAuth flow
        group.MapGet("/google", async (HttpContext ctx, string? returnUrl) =>
        {
            var redirectUrl = $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            await ctx.ChallengeAsync(GoogleDefaults.AuthenticationScheme, properties);
        });

        // POST /api/auth/sign-out
        group.MapPost("/sign-out", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
}