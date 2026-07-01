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
            RunScopeDbContext db,
            IConfiguration config,
            IHttpClientFactory httpClientFactory) =>
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

            var (waypoints, segments) = BuildWaypointsAndSegments(route.Id, request.Waypoints, request.Segments);

            // Fetch elevation for each waypoint from Mapbox
            var mapboxToken = config["Mapbox:AccessToken"];
            if (!string.IsNullOrEmpty(mapboxToken))
            {
                await EnrichWithElevation(waypoints, mapboxToken, httpClientFactory);
                route.ElevationGainMeters = CalculateElevationGain(waypoints);
            }

            route.Waypoints = waypoints;
            route.Segments = segments;

            db.Routes.Add(route);
            await db.SaveChangesAsync();

            return Results.Created($"/api/routes/{route.Id}", ToDto(route));
        });

        // PUT /api/routes/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpsertRouteRequest request,
            HttpContext ctx,
            RunScopeDbContext db,
            IConfiguration config,
            IHttpClientFactory httpClientFactory) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (route is null) return Results.NotFound();

            route.Name = request.Name;
            route.TotalDistance = request.TotalDistance;
            route.UpdatedAt = DateTime.UtcNow;

            var existingWaypoints = await db.RouteWaypoints
                .Where(w => w.RouteId == id)
                .ToListAsync();
            var existingSegments = await db.RouteSegments
                .Where(s => s.RouteId == id)
                .ToListAsync();

            db.RouteWaypoints.RemoveRange(existingWaypoints);
            db.RouteSegments.RemoveRange(existingSegments);
            await db.SaveChangesAsync();

            var (newWaypoints, newSegments) = BuildWaypointsAndSegments(route.Id, request.Waypoints, request.Segments);

            // Fetch elevation for each waypoint from Mapbox
            var mapboxToken = config["Mapbox:AccessToken"];
            if (!string.IsNullOrEmpty(mapboxToken))
            {
                await EnrichWithElevation(newWaypoints, mapboxToken, httpClientFactory);
                route.ElevationGainMeters = CalculateElevationGain(newWaypoints);
            }

            db.RouteWaypoints.AddRange(newWaypoints);
            db.RouteSegments.AddRange(newSegments);
            await db.SaveChangesAsync();

            route.Waypoints = newWaypoints;
            route.Segments = newSegments;

            return Results.Ok(ToDto(route));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            RunScopeDbContext db,
            bool force = false) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (route is null) return Results.NotFound();

            if (!force)
            {
                var hasLinkedEvents = await db.Events.AnyAsync(e => e.RouteId == id);
                if (hasLinkedEvents)
                    return Results.Conflict(
                        "This route has past events linked to it and cannot be deleted. " +
                        "You can still edit the route.");
            }
            else
            {
                // Force delete — remove linked events first
                var linkedEvents = await db.Events
                    .Where(e => e.RouteId == id)
                    .ToListAsync(); ;
                db.Events.RemoveRange(linkedEvents);
            }

            db.Routes.Remove(route);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
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
                ElevationGainMeters = source.ElevationGainMeters,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Waypoints = source.Waypoints.Select(w => new RouteWaypoint
                {
                    Id = Guid.NewGuid(),
                    Order = w.Order,
                    Longitude = w.Longitude,
                    Latitude = w.Latitude,
                    ElevationMeters = w.ElevationMeters,
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

    // ── Elevation helpers ───────────────────────────────────────────────────

    private static async Task EnrichWithElevation(
        List<RouteWaypoint> waypoints,
        string mapboxToken,
        IHttpClientFactory httpClientFactory)
    {
        var client = httpClientFactory.CreateClient();

        // Mapbox Tilequery API — terrain-rgb tileset gives elevation in meters
        // Process in parallel but respect rate limits with a small batch delay
        var tasks = waypoints.Select(async (wp, i) =>
        {
            // Stagger requests slightly to avoid rate limiting
            await Task.Delay(i * 50);

            var url = $"https://api.mapbox.com/v4/mapbox.mapbox-terrain-v2/tilequery/{wp.Longitude},{wp.Latitude}.json?layers=contour&limit=1&access_token={mapboxToken}";

            try
            {
                var response = await client.GetStringAsync(url);
                var json = JsonDocument.Parse(response);
                var features = json.RootElement.GetProperty("features");

                if (features.GetArrayLength() > 0)
                {
                    var ele = features[0]
                        .GetProperty("properties")
                        .GetProperty("ele");

                    wp.ElevationMeters = ele.GetDouble();
                }
            }
            catch
            {
                // If elevation lookup fails for a waypoint, leave it null
                // rather than failing the entire route save
            }
        });

        await Task.WhenAll(tasks);
    }

    private static double? CalculateElevationGain(List<RouteWaypoint> waypoints)
    {
        var ordered = waypoints.OrderBy(w => w.Order).ToList(); ;

        // Need at least 2 waypoints with elevation data
        if (ordered.Count < 2 || ordered.Any(w => w.ElevationMeters is null))
            return null;

        double gain = 0;
        for (int i = 1; i < ordered.Count; i++)
        {
            var diff = ordered[i].ElevationMeters!.Value - ordered[i - 1].ElevationMeters!.Value;
            if (diff > 0) gain += diff;
        }

        return gain;
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    private static (List<RouteWaypoint> waypoints, List<RouteSegment> segments) BuildWaypointsAndSegments(
        Guid routeId,
        List<WaypointRequest> waypointRequests,
        List<SegmentRequest> segmentRequests)
    {
        var idMap = waypointRequests.ToDictionary(
            w => w.ClientId,
            w => Guid.NewGuid()
        );

        var waypoints = waypointRequests.Select(w => new RouteWaypoint
        {
            Id = idMap[w.ClientId],
            RouteId = routeId,
            Order = w.Order,
            Longitude = w.Coordinates[0],
            Latitude = w.Coordinates[1],
        }).ToList();

        var segments = segmentRequests.Select(s => new RouteSegment
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            FromWaypointId = idMap.TryGetValue(s.FromWaypointId, out var fromId) ? fromId : s.FromWaypointId,
            ToWaypointId = idMap.TryGetValue(s.ToWaypointId, out var toId) ? toId : s.ToWaypointId,
            Distance = s.Distance,
            PathJson = JsonSerializer.Serialize(s.Path),
        }).ToList();

        return (waypoints, segments);
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
        elevationGainMeters = r.ElevationGainMeters,
        createdAt = FormatUtc(r.CreatedAt),
        updatedAt = FormatUtc(r.UpdatedAt),
        waypoints = r.Waypoints
            .OrderBy(w => w.Order)
            .Select(w => new
            {
                id = w.Id,
                order = w.Order,
                coordinates = new[] { w.Longitude, w.Latitude },
                elevationMeters = w.ElevationMeters,
            }),
        segments = r.Segments.Select(s => new
        {
            fromWaypointId = s.FromWaypointId,
            toWaypointId = s.ToWaypointId,
            distance = s.Distance,
            path = JsonSerializer.Deserialize<double[][]>(s.PathJson),
        }),
    };

    private static string FormatUtc(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O");
}

public record WaypointRequest(Guid ClientId, int Order, double[] Coordinates);
public record SegmentRequest(Guid FromWaypointId, Guid ToWaypointId, double Distance, double[][] Path);
public record UpsertRouteRequest(string Name, double TotalDistance, List<WaypointRequest> Waypoints, List<SegmentRequest> Segments);