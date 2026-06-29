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

        // GET /api/users/me/prs — get all personal records for current user
        group.MapGet("/me/prs", async (HttpContext ctx, RunScopeDbContext db) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var prs = await db.PersonalRecords
                .Where(pr => pr.UserId == userId)
                .ToListAsync();

            return Results.Ok(prs.Select(pr => new
            {
                distance = pr.Distance.ToString(),
                timeSeconds = pr.TimeSeconds,
                updatedAt = pr.UpdatedAt.ToString("O"),
            }));
        });

        // PUT /api/users/me/prs/{distance} — upsert a personal record
        group.MapPut("/me/prs/{distance}", async (
            string distance,
            HttpContext ctx,
            RunScopeDbContext db,
            UpsertPrRequest request) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            if (!Enum.TryParse<RaceDistance>(distance, ignoreCase: true, out var raceDistance))
                return Results.BadRequest("Invalid distance.");

            if (request.TimeSeconds <= 0)
                return Results.BadRequest("Time must be greater than zero.");

            var pr = await db.PersonalRecords
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Distance == raceDistance);

            if (pr is null)
            {
                pr = new PersonalRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Distance = raceDistance,
                    TimeSeconds = request.TimeSeconds,
                    UpdatedAt = DateTime.UtcNow,
                };
                db.PersonalRecords.Add(pr);
            }
            else
            {
                pr.TimeSeconds = request.TimeSeconds;
                pr.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                distance = pr.Distance.ToString(),
                timeSeconds = pr.TimeSeconds,
                updatedAt = pr.UpdatedAt.ToString("O"),
            });
        });

        // DELETE /api/users/me/prs/{distance} — remove a personal record
        group.MapDelete("/me/prs/{distance}", async (
            string distance,
            HttpContext ctx,
            RunScopeDbContext db) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            if (!Enum.TryParse<RaceDistance>(distance, ignoreCase: true, out var raceDistance))
                return Results.BadRequest("Invalid distance.");

            var pr = await db.PersonalRecords
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Distance == raceDistance);

            if (pr is null) return Results.NotFound();

            db.PersonalRecords.Remove(pr);
            await db.SaveChangesAsync();

            return Results.NoContent();
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
public record UpsertPrRequest(int TimeSeconds);