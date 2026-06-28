using Microsoft.EntityFrameworkCore;
using RunScope.Core.Models;

namespace RunScope.Core.Data;

public class RunScopeDbContext(DbContextOptions<RunScopeDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteWaypoint> RouteWaypoints => Set<RouteWaypoint>();
    public DbSet<RouteSegment> RouteSegments => Set<RouteSegment>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventLocation> EventLocations => Set<EventLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.GoogleId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Route>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.HasOne(r => r.User)
                .WithMany(u => u.Routes)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RouteWaypoint>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).ValueGeneratedOnAdd();
            e.HasOne(w => w.Route)
                .WithMany(r => r.Waypoints)
                .HasForeignKey(w => w.RouteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RouteSegment>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedOnAdd();
            e.HasOne(s => s.Route)
                .WithMany(r => r.Segments)
                .HasForeignKey(s => s.RouteId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.PathJson).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Id).ValueGeneratedOnAdd();
            e.HasIndex(ev => ev.EventCode).IsUnique();
            e.Property(ev => ev.Status).HasConversion<string>();
            e.Property(ev => ev.CancelReason).HasConversion<string>();
            e.HasOne(ev => ev.User)
                .WithMany(u => u.Events)
                .HasForeignKey(ev => ev.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(ev => ev.Route)
                .WithMany(r => r.Events)
                .HasForeignKey(ev => ev.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventLocation>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.HasOne(l => l.Event)
                .WithMany(ev => ev.Locations)
                .HasForeignKey(l => l.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.EventId, l.Timestamp });
        });
    }
}