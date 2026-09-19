using System.Security.Claims;
using ApplicationAssistant.AI;
using ApplicationAssistant.Api;
using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.FileProviders;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

if (args.Any(a => string.Equals(a, "migrate-dates", StringComparison.OrdinalIgnoreCase)))
{
    var migrateBuilder = WebApplication.CreateBuilder(args);
    migrateBuilder.Services.AddSingleton<MongoDbContext>();
    await using var migrateApp = migrateBuilder.Build();
    var mongo = migrateApp.Services.GetRequiredService<MongoDbContext>();
    Console.WriteLine("Running profile date migration…");
    var result = await ProfileDateMigration.RunAsync(mongo.Database);
    Console.WriteLine(
        $"Done. Users updated: {result.UsersTouched}, fields converted: {result.FieldsConverted}, failed: {result.FieldsFailed}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

var frontendOrigin = builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173";
var googleClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
var isProduction = builder.Environment.IsProduction();

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<CvPdfRenderer>();
builder.Services.AddApplicationAssistantAi();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "aa_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax;
        options.Cookie.SecurePolicy = isProduction
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId = string.IsNullOrWhiteSpace(googleClientId)
            ? "missing-google-client-id"
            : googleClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(googleClientSecret)
            ? "missing-google-client-secret"
            : googleClientSecret;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = true;
        options.Events.OnCreatingTicket = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<MongoDbContext>();
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email)
                ?? context.Principal?.FindFirstValue("email");
            var name = context.Principal?.FindFirstValue(ClaimTypes.Name)
                ?? context.Principal?.FindFirstValue("name");
            var googleId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Principal?.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(googleId))
            {
                return;
            }

            var user = await db.Users.Find(u => u.GoogleId == googleId).FirstOrDefaultAsync();
            if (user is null)
            {
                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    GoogleId = googleId,
                    Email = email,
                    DisplayName = name ?? email,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await db.Users.InsertOneAsync(user);
            }

            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            identity.AddClaim(new Claim("app_user_id", user.Id.ToString()));
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(frontendOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await db.EnsureIndexesAsync();
}

app.UseCors("Frontend");

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

var spaPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(spaPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

if (Directory.Exists(spaPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
