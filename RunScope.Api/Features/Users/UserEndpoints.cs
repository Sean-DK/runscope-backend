using Microsoft.EntityFrameworkCore;
using RunScope.Core.Data;
using RunScope.Core.Models;
using System.Security.Claims;

namespace RunScope.Api.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        // GET /api/users/me
        group.MapGet("/me", async (HttpContext ctx, RunScopeDbContext db) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.NotFound();

            return Results.Ok(ToDto(user));
        });

        // PATCH /api/users/me/preferences
        group.MapPatch("/me/preferences", async (
            HttpContext ctx,
            RunScopeDbContext db,
            UpdatePreferencesRequest request) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.NotFound();

            if (Enum.TryParse<UnitPreference>(request.UnitPreference, ignoreCase: true, out var parsed))
                user.UnitPreference = parsed;

            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(ToDto(user));
        });
    }

    private static object ToDto(User user) => new
    {
        id = user.Id,
        email = user.Email,
        name = user.Name,
        avatarUrl = user.AvatarUrl,
        unitPreference = user.UnitPreference.ToString(),
    };
}

public record UpdatePreferencesRequest(string UnitPreference);