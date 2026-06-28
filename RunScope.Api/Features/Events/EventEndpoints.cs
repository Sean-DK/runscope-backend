using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RunScope.Api.Hubs;
using RunScope.Core.Data;
using RunScope.Core.Models;
using System.Security.Claims;

namespace RunScope.Api.Features.Events;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events");

        // POST /api/events — create event (racer only)
        group.MapPost("/", async (
            [FromBody] CreateEventRequest request,
            HttpContext ctx,
            RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var route = await db.Routes
                .Include(r => r.Waypoints)
                .Include(r => r.Segments)
                .FirstOrDefaultAsync(r => r.Id == request.RouteId && r.UserId == userId);

            if (route is null) return Results.NotFound("Route not found.");

            var eventCode = GenerateEventCode();

            // Ensure code is unique
            while (await db.Events.AnyAsync(e => e.EventCode == eventCode))
                eventCode = GenerateEventCode();

            var ev = new Event
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                RouteId = route.Id,
                EventCode = eventCode,
                Status = EventStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };

            db.Events.Add(ev);
            await db.SaveChangesAsync();

            return Results.Created($"/api/events/{ev.Id}", ToDto(ev, route));
        }).RequireAuthorization();

        // GET /api/events/{id} — get event by ID (spectator join via link)
        group.MapGet("/{id:guid}", async (Guid id, RunScopeDbContext db) =>
        {
            var ev = await db.Events
                .Include(e => e.Route)
                    .ThenInclude(r => r.Waypoints)
                .Include(e => e.Route)
                    .ThenInclude(r => r.Segments)
                .Include(e => e.Locations.OrderByDescending(l => l.Timestamp).Take(1))
                .FirstOrDefaultAsync(e => e.Id == id);

            if (ev is null) return Results.NotFound();
            if (ev.Status == EventStatus.Ended) return Results.NotFound("Event has ended.");

            return Results.Ok(ToDto(ev, ev.Route));
        });

        // GET /api/events/code/{code} — get event by code (spectator manual join)
        group.MapGet("/code/{code}", async (string code, RunScopeDbContext db) =>
        {
            var ev = await db.Events
                .Include(e => e.Route)
                    .ThenInclude(r => r.Waypoints)
                .Include(e => e.Route)
                    .ThenInclude(r => r.Segments)
                .Include(e => e.Locations.OrderByDescending(l => l.Timestamp).Take(1))
                .FirstOrDefaultAsync(e =>
                    e.EventCode == code.ToUpper() &&
                    e.Status != EventStatus.Ended);

            return ev is null ? Results.NotFound() : Results.Ok(ToDto(ev, ev.Route));
        });

        // GET /api/events/past — racer's past events
        group.MapGet("/past", async (HttpContext ctx, RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var events = await db.Events
                .Where(e =>
                    e.UserId == userId &&
                    (e.Status == EventStatus.Finished || e.Status == EventStatus.Cancelled))
                .Include(e => e.Route)
                .Include(e => e.Locations.OrderByDescending(l => l.Timestamp).Take(1))
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return Results.Ok(events.Select(e => ToDto(e, e.Route)));
        }).RequireAuthorization();

        // GET /api/events/past/{id} — past event detail
        group.MapGet("/past/{id:guid}", async (Guid id, HttpContext ctx, RunScopeDbContext db) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var ev = await db.Events
                .Include(e => e.Route)
                    .ThenInclude(r => r.Waypoints)
                .Include(e => e.Route)
                    .ThenInclude(r => r.Segments)
                .Include(e => e.Locations.OrderByDescending(l => l.Timestamp).Take(1))
                .FirstOrDefaultAsync(e =>
                    e.Id == id &&
                    e.UserId == userId &&
                    (e.Status == EventStatus.Finished || e.Status == EventStatus.Cancelled));

            return ev is null ? Results.NotFound() : Results.Ok(ToDto(ev, ev.Route));
        }).RequireAuthorization();

        // PATCH /api/events/{id}/status
        group.MapPatch("/{id:guid}/status", async (
            Guid id,
            [FromBody] UpdateStatusRequest request,
            HttpContext ctx,
            RunScopeDbContext db,
            IHubContext<SpectatorHub> hub) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var ev = await db.Events
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (ev is null) return Results.NotFound();

            ev.Status = request.Status;
            if (request.StartedAt.HasValue) ev.StartedAt = request.StartedAt;
            if (request.FinishedAt.HasValue) ev.FinishedAt = request.FinishedAt;
            if (request.EndedAt.HasValue) ev.EndedAt = request.EndedAt;
            if (request.CancelReason.HasValue) ev.CancelReason = request.CancelReason;

            await db.SaveChangesAsync();

            // Broadcast status change to all spectators watching this event
            await hub.Clients
                .Group(id.ToString())
                .SendAsync("StatusUpdate", new
                {
                    status = ev.Status.ToString(),
                    cancelReason = ev.CancelReason?.ToString(),
                    startedAt = ev.StartedAt,
                    finishedAt = ev.FinishedAt,
                    endedAt = ev.EndedAt,
                });

            return Results.Ok(new { status = ev.Status.ToString() });
        }).RequireAuthorization();

        // POST /api/events/{id}/locations — push location update (racer only)
        group.MapPost("/{id:guid}/locations", async (
            Guid id,
            [FromBody] PushLocationRequest request,
            HttpContext ctx,
            RunScopeDbContext db,
            IHubContext<SpectatorHub> hub) =>
        {
            var userId = GetUserId(ctx);
            if (userId is null) return Results.Unauthorized();

            var ev = await db.Events
                .FirstOrDefaultAsync(e =>
                    e.Id == id &&
                    e.UserId == userId &&
                    (e.Status == EventStatus.Pending || e.Status == EventStatus.Active));

            if (ev is null) return Results.NotFound();

            var location = new EventLocation
            {
                Id = Guid.NewGuid(),
                EventId = id,
                Longitude = request.Coordinates[0],
                Latitude = request.Coordinates[1],
                DistanceFromStart = request.DistanceFromStart,
                CurrentPaceSecondsPerMile = request.CurrentPaceSecondsPerMile,
                AveragePaceSecondsPerMile = request.AveragePaceSecondsPerMile,
                Timestamp = request.Timestamp,
            };

            db.EventLocations.Add(location);
            await db.SaveChangesAsync();

            // Broadcast to spectators via SignalR
            await hub.Clients
                .Group(id.ToString())
                .SendAsync("LocationUpdate", new
                {
                    coordinates = request.Coordinates,
                    timestamp = request.Timestamp,
                    distanceFromStart = request.DistanceFromStart,
                    currentPaceSecondsPerMile = request.CurrentPaceSecondsPerMile,
                    averagePaceSecondsPerMile = request.AveragePaceSecondsPerMile,
                });

            return Results.Ok();
        }).RequireAuthorization();
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.TryParse(claim, out var id) ? id : null;
    }

    private static string GenerateEventCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }

    private static object ToDto(Event ev, Core.Models.Route route) => new
    {
        id = ev.Id,
        eventCode = ev.EventCode,
        routeId = ev.RouteId,
        route = new
        {
            id = route.Id,
            name = route.Name,
            totalDistance = route.TotalDistance,
            createdAt = route.CreatedAt,
            updatedAt = route.UpdatedAt,
            waypoints = route.Waypoints
                .OrderBy(w => w.Order)
                .Select(w => new
                {
                    id = w.Id,
                    order = w.Order,
                    coordinates = new[] { w.Longitude, w.Latitude },
                }),
            segments = route.Segments.Select(s => new
            {
                fromWaypointId = s.FromWaypointId,
                toWaypointId = s.ToWaypointId,
                distance = s.Distance,
                path = System.Text.Json.JsonSerializer.Deserialize<double[][]>(s.PathJson),
            }),
        },
        status = ev.Status.ToString(),
        cancelReason = ev.CancelReason?.ToString(),
        createdAt = ev.CreatedAt,
        startedAt = ev.StartedAt,
        finishedAt = ev.FinishedAt,
        endedAt = ev.EndedAt,
        lastLocation = ev.Locations.FirstOrDefault() is { } loc ? new
        {
            coordinates = new[] { loc.Longitude, loc.Latitude },
            timestamp = loc.Timestamp,
            distanceFromStart = loc.DistanceFromStart,
            currentPaceSecondsPerMile = loc.CurrentPaceSecondsPerMile,
            averagePaceSecondsPerMile = loc.AveragePaceSecondsPerMile,
        } : null,
    };
}

public record CreateEventRequest(Guid RouteId);
public record UpdateStatusRequest(
    EventStatus Status,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime? EndedAt,
    CancelReason? CancelReason);
public record PushLocationRequest(
    double[] Coordinates,
    double DistanceFromStart,
    double? CurrentPaceSecondsPerMile,
    double? AveragePaceSecondsPerMile,
    DateTime Timestamp);