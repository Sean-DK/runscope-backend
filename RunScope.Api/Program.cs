using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using RunScope.Api.Features.Auth;
using RunScope.Api.Features.Events;
using RunScope.Api.Features.Routes;
using RunScope.Api.Features.Users;
using RunScope.Api.Hubs;
using RunScope.Core.Data;
using RunScope.Core.Models;
using System.Security.Claims;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Database
builder.Services.AddDbContext<RunScopeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Auth
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // Return 401 instead of redirecting to login for API routes
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Auth:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"]!;
    options.CallbackPath = "/api/auth/google/callback";
    options.SaveTokens = false;

    options.CorrelationCookie.HttpOnly = true;
    options.CorrelationCookie.SameSite = SameSiteMode.None;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    options.CorrelationCookie.IsEssential = true;
    options.CorrelationCookie.Path = "/";

    options.Events.OnTicketReceived = async ctx =>
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<RunScopeDbContext>();
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

        var googleId = ctx.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email = ctx.Principal!.FindFirstValue(ClaimTypes.Email)!;
        var name = ctx.Principal!.FindFirstValue(ClaimTypes.Name)!;
        var avatarUrl = ctx.Principal!.FindFirstValue("picture");

        // Upsert user
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                GoogleId = googleId,
                Email = email,
                Name = name,
                AvatarUrl = avatarUrl,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
        }
        else
        {
            user.Name = name;
            user.AvatarUrl = avatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Replace principal with one containing the database user ID
        var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Email,          user.Email),
        new(ClaimTypes.Name,           user.Name),
    };
        if (user.AvatarUrl is not null)
            claims.Add(new("picture", user.AvatarUrl));

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);
        ctx.Principal = new ClaimsPrincipal(identity);

        // Generate a short-lived one-time token for native app auth.
        // Included in the redirect URL so the Capacitor webview can exchange
        // it for a session cookie via POST /api/auth/exchange.
        var tokenBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", "");

        db.OneTimeTokens.Add(new OneTimeToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Used = false,
        });
        await db.SaveChangesAsync();

        var frontendBase = config["Cors:AllowedOrigins"]?.Split(',').First()
            ?? "https://runscope.stablesea.net";
        var returnUrl = ctx.Properties?.RedirectUri ?? "/";
        ctx.ReturnUri = $"{frontendBase}/auth/callback?returnUrl={Uri.EscapeDataString(returnUrl)}&token={token}";
    };
});

builder.Services.AddAuthorization();

// SignalR
builder.Services.AddSignalR();

// CORS — allow React dev server and production frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration["Cors:AllowedOrigins"]?
                    .Split(',') ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for cookies + SignalR
    });
});

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// Map feature endpoints
app.MapAuthEndpoints();
app.MapRouteEndpoints();
app.MapEventEndpoints();
app.MapUserEndpoints();

// SignalR hub
app.MapHub<SpectatorHub>("/hubs/spectator");

// Allow running migrations from CLI: dotnet RunScope.Api.dll migrate
if (args.Contains("migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RunScopeDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("Migrations applied successfully.");
    return;
}

app.Run();