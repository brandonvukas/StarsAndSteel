using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StarsAndSteel.Api.Auth;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence ----------------------------------------------------------
// Connection string is named "StarsAndSteelDb" to match the DB name. In Development
// this resolves via user-secrets to the local SQL Server; production overrides via
// env var. Never committed to the repo.
var connectionString = builder.Configuration.GetConnectionString("StarsAndSteelDb")
    ?? throw new InvalidOperationException(
        "Connection string 'StarsAndSteelDb' is not configured. " +
        "Set it via user-secrets (dev) or environment (prod).");

builder.Services.AddDbContext<StarsAndSteelDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- Identity -------------------------------------------------------------
// MUST use the generic AddIdentity<User, IdentityRole<Guid>>(...) overload so Guid keys
// flow through. Using the non-generic AddIdentity() would silently revert to string PKs
// and the Migration 1 diff would not match our entity model.
builder.Services
    .AddIdentity<User, IdentityRole<Guid>>(options =>
    {
        // Match docs/10 §"Password requirements": Identity defaults are deliberately strict.
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Lock out for 5 min after 5 bad attempts (cheap brute-force defense).
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<StarsAndSteelDbContext>()
    .AddDefaultTokenProviders();

// --- Auth: cookie for SPA + JWT for SignalR (see docs/10 §"Authentication") ----------
// Bind JwtOptions and validate eagerly so missing config fails fast at startup
// rather than the first request.
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Key),
        "Jwt:Key must be configured (user-secrets in dev, STARSANDSTEEL_JWT_KEY in prod).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be configured.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<ITokenService, TokenService>();

// AddIdentity registers the cookie scheme as the default. We override the cookie
// behavior so unauthenticated API calls get 401/403 (instead of redirects to a
// login page that doesn't exist in this SPA-style app).
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.Name = "stars_and_steel_auth";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Register JWT bearer alongside the cookie. The default scheme stays as Identity
// cookies (so [Authorize] on REST endpoints works without ceremony), but endpoints
// that explicitly want JWT (e.g., SignalR hub later) can ask for the Bearer scheme.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];
var jwtKeyRaw = jwtSection["Key"];
byte[] jwtKeyBytes;
if (!string.IsNullOrWhiteSpace(jwtKeyRaw))
{
    try
    {
        jwtKeyBytes = Convert.FromBase64String(jwtKeyRaw);
    }
    catch (FormatException)
    {
        jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKeyRaw);
    }
}
else
{
    // Empty placeholder; the IOptions<JwtOptions> validator will refuse to start
    // and TokenService will refuse to issue. We just need *something* here to
    // construct TokenValidationParameters during DI assembly.
    jwtKeyBytes = new byte[32];
}

builder.Services
    .AddAuthentication() // do NOT pass a default scheme — keep cookies as the default
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SignalR pattern: pull the token from the access_token query string when
        // the request targets a hub. We don't have hubs yet (Phase 1F+) but wiring
        // it now means we don't have to revisit this file.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });

// --- FluentValidation -----------------------------------------------------
// Validators in this assembly are auto-discovered. Controllers resolve
// IValidator<T> directly (we don't use the legacy auto-validating filter).
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// --- Rate limiting (docs/10 §"Rate limiting") -----------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 5 requests/minute/IP for /api/auth/* — defends register/login from brute force.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
});

// --- ASP.NET Core --------------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
