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

        // GET /api/auth/me
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
                unitPreference = user.UnitPreference.ToString(),
            });
        }).RequireAuthorization();

        // GET /api/auth/google — initiates OAuth flow
        group.MapGet("/google", (HttpContext ctx, string? returnUrl) =>
        {
            var redirectUrl = $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        // POST /api/auth/exchange — exchange one-time token for session cookie (native app)
        group.MapPost("/exchange", async (
            HttpContext ctx,
            RunScopeDbContext db,
            ExchangeTokenRequest request) =>
        {
            var ott = await db.OneTimeTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t =>
                    t.Token == request.Token &&
                    !t.Used &&
                    t.ExpiresAt > DateTime.UtcNow);

            if (ott is null)
                return Results.BadRequest("Invalid or expired token.");

            ott.Used = true;
            await db.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, ott.User.Id.ToString()),
                new(ClaimTypes.Email,          ott.User.Email),
                new(ClaimTypes.Name,           ott.User.Name),
            };
            if (ott.User.AvatarUrl is not null)
                claims.Add(new("picture", ott.User.AvatarUrl));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                });

            return Results.Ok(new
            {
                id = ott.User.Id,
                email = ott.User.Email,
                name = ott.User.Name,
                avatarUrl = ott.User.AvatarUrl,
                unitPreference = ott.User.UnitPreference.ToString(),
            });
        });

        // POST /api/auth/sign-out
        group.MapPost("/sign-out", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
}

public record ExchangeTokenRequest(string Token);