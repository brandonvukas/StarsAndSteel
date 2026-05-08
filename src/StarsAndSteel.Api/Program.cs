using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence ----------------------------------------------------------
// Connection string is named "StarsAndSteelDb" to match the DB name. In Development
// this resolves to (localdb)\MSSQLLocalDB; production will override via env var
// or a secrets store.
var connectionString = builder.Configuration.GetConnectionString("StarsAndSteelDb")
    ?? throw new InvalidOperationException(
        "Connection string 'StarsAndSteelDb' is not configured. " +
        "Set it in appsettings.Development.json or via environment.");

builder.Services.AddDbContext<StarsAndSteelDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- Identity -------------------------------------------------------------
// MUST use the generic AddIdentity<User, IdentityRole<Guid>>(...) overload so Guid keys
// flow through. Using the non-generic AddIdentity() would silently revert to string PKs
// and the Migration 1 diff would not match our entity model.
builder.Services
    .AddIdentity<User, IdentityRole<Guid>>(options =>
    {
        // Tighten password rules in Phase 1D when /api/auth/* lands. Defaults are fine for now.
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<StarsAndSteelDbContext>()
    .AddDefaultTokenProviders();

// --- ASP.NET Core --------------------------------------------------------

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
