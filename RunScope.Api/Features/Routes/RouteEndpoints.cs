using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunScope.Core.Data;
using RunScope.Core.Models;
using System.Security.Claims;
using System.Text.Json;

namespace RunScope.Api.Features.Routes;

public static class RouteEndpoints
{
    public static void MapRouteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/routes").RequireAuthorization();

        // GET /api/routes
        group.MapGet("/", async (HttpContext ctx, RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var routes = await db.Routes
                .Where(r => r.UserId == userId)
                .Include(r => r.Waypoints)
                .Include(r => r.Segments)
                .OrderByDescending(r => r.UpdatedAt)
                .ToListAsync();

            return Results.Ok(routes.Select(ToDto));
        });

        // GET /api/routes/{id}
        group.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .Include(r => r.Waypoints)
                .Include(r => r.Segments)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            return route is null ? Results.NotFound() : Results.Ok(ToDto(route));
        });

        // GET /api/routes/shared/{id} — public, no auth required
        app.MapGet("/api/routes/shared/{id:guid}", async (Guid id, RunScopeDbContext db) =>
        {
            var route = await db.Routes
                .Include(r => r.Waypoints)
                .Include(r => r.Segments)
                .FirstOrDefaultAsync(r => r.Id == id);

            return route is null ? Results.NotFound() : Results.Ok(ToDto(route));
        });

        // POST /api/routes
        group.MapPost("/", async (
            [FromBody] UpsertRouteRequest request,
            HttpContext ctx,
            RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = new Core.Models.Route
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = request.Name,
                TotalDistance = request.TotalDistance,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            route.Waypoints = request.Waypoints.Select((w, i) => new RouteWaypoint
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                Order = w.Order,
                Longitude = w.Coordinates[0],
                Latitude = w.Coordinates[1],
            }).ToList();

            route.Segments = request.Segments.Select(s => new RouteSegment
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                FromWaypointId = s.FromWaypointId,
                ToWaypointId = s.ToWaypointId,
                Distance = s.Distance,
                PathJson = JsonSerializer.Serialize(s.Path),
            }).ToList();

            db.Routes.Add(route);
            await db.SaveChangesAsync();

            return Results.Created($"/api/routes/{route.Id}", ToDto(route));
        });

        // PUT /api/routes/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpsertRouteRequest request,
            HttpContext ctx,
            RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (route is null) return Results.NotFound();

            // Update scalar properties
            route.Name = request.Name;
            route.TotalDistance = request.TotalDistance;
            route.UpdatedAt = DateTime.UtcNow;

            // Load and delete existing waypoints and segments separately
            // to avoid EF Core concurrency tracking issues
            var existingWaypoints = await db.RouteWaypoints
                .Where(w => w.RouteId == id)
                .ToListAsync();
            var existingSegments = await db.RouteSegments
                .Where(s => s.RouteId == id)
                .ToListAsync();

            db.RouteWaypoints.RemoveRange(existingWaypoints);
            db.RouteSegments.RemoveRange(existingSegments);

            // Save deletions before adding new ones to avoid PK conflicts
            await db.SaveChangesAsync();

            // Add new waypoints and segments
            var newWaypoints = request.Waypoints.Select(w => new RouteWaypoint
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                Order = w.Order,
                Longitude = w.Coordinates[0],
                Latitude = w.Coordinates[1],
            }).ToList();

            var newSegments = request.Segments.Select(s => new RouteSegment
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                FromWaypointId = s.FromWaypointId,
                ToWaypointId = s.ToWaypointId,
                Distance = s.Distance,
                PathJson = JsonSerializer.Serialize(s.Path),
            }).ToList();

            db.RouteWaypoints.AddRange(newWaypoints);
            db.RouteSegments.AddRange(newSegments);

            await db.SaveChangesAsync();

            // Reload for the response DTO
            route.Waypoints = newWaypoints;
            route.Segments = newSegments;

            return Results.Ok(ToDto(route));
        });

        // DELETE /api/routes/{id}
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (route is null) return Results.NotFound();

            // Check for linked events before attempting delete
            var hasLinkedEvents = await db.Events.AnyAsync(e => e.RouteId == id);
            if (hasLinkedEvents)
                return Results.Conflict(
                    "This route has past events linked to it and cannot be deleted. " +
                    "You can still edit the route.");

            db.Routes.Remove(route);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Fallback in case the AnyAsync check races with a concurrent insert
                return Results.Conflict(
                    "This route could not be deleted because it has linked events.");
            }

            return Results.NoContent();
        });

        // POST /api/routes/shared/{id}/save — save a shared route as a copy
        app.MapPost("/api/routes/shared/{id:guid}/save", async (
            Guid id,
            HttpContext ctx,
            RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var source = await db.Routes
                .Include(r => r.Waypoints)
                .Include(r => r.Segments)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (source is null) return Results.NotFound();

            var copy = new Core.Models.Route
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = source.Name,
                TotalDistance = source.TotalDistance,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Waypoints = source.Waypoints.Select(w => new RouteWaypoint
                {
                    Id = Guid.NewGuid(),
                    Order = w.Order,
                    Longitude = w.Longitude,
                    Latitude = w.Latitude,
                }).ToList(),
                Segments = source.Segments.Select(s => new RouteSegment
                {
                    Id = Guid.NewGuid(),
                    FromWaypointId = s.FromWaypointId,
                    ToWaypointId = s.ToWaypointId,
                    Distance = s.Distance,
                    PathJson = s.PathJson,
                }).ToList(),
            };

            db.Routes.Add(copy);
            await db.SaveChangesAsync();

            return Results.Created($"/api/routes/{copy.Id}", ToDto(copy));
        }).RequireAuthorization();
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.TryParse(claim, out var id) ? id : null;
    }

    private static object ToDto(Core.Models.Route r) => new
    {
        id = r.Id,
        name = r.Name,
        totalDistance = r.TotalDistance,
        createdAt = r.CreatedAt,
        updatedAt = r.UpdatedAt,
        waypoints = r.Waypoints
            .OrderBy(w => w.Order)
            .Select(w => new
            {
                id = w.Id,
                order = w.Order,
                coordinates = new[] { w.Longitude, w.Latitude },
            }),
        segments = r.Segments.Select(s => new
        {
            fromWaypointId = s.FromWaypointId,
            toWaypointId = s.ToWaypointId,
            distance = s.Distance,
            path = JsonSerializer.Deserialize<double[][]>(s.PathJson),
        }),
    };
}

public record WaypointRequest(int Order, double[] Coordinates);
public record SegmentRequest(Guid FromWaypointId, Guid ToWaypointId, double Distance, double[][] Path);
public record UpsertRouteRequest(string Name, double TotalDistance, List<WaypointRequest> Waypoints, List<SegmentRequest> Segments);