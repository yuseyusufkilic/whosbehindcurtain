using System.Threading.RateLimiting;
using HiddenSeason.Api.Domain;
using HiddenSeason.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var herokuPort))
{
    builder.WebHost.UseUrls($"http://+:{herokuPort}");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 16 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 90,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 45,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("mutation", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

if (builder.Configuration.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    });
}

builder.Services.AddSingleton(sp => PuzzleCatalog.Load(
    builder.Environment.ContentRootPath,
    builder.Configuration["Game:CatalogPath"] ?? "Data/puzzles.json"));
builder.Services.AddSingleton<AnonymousSessionService>();
builder.Services.AddSingleton<FileGameProgressStore>();

var app = builder.Build();

app.UseExceptionHandler();
if (app.Configuration.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    app.UseForwardedHeaders();
}
app.UseCors();
app.UseRateLimiter();
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/puzzles/daily", async (
    HttpContext context,
    PuzzleCatalog catalog,
    AnonymousSessionService anonymousSessions,
    FileGameProgressStore progress,
    CancellationToken cancellationToken) =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var puzzle = catalog.GetDaily(today);
    if (puzzle is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Günün oyuncusu henüz hazır değil.",
            detail: $"{today:yyyy-MM-dd} tarihi için puzzle kataloğu oluşturulmamış.");
    }

    var subjectId = anonymousSessions.GetOrCreate(context);
    var response = await progress.ExecuteAsync(
        subjectId,
        puzzle.Id,
        session => session.ToPuzzleResponse(puzzle),
        cancellationToken);
    var stats = await progress.GetStatsAsync(subjectId, cancellationToken);
    return Results.Ok(response with { Stats = stats });
}).RequireRateLimiting("read");

api.MapGet("/puzzles/archive", async (
    HttpContext context,
    PuzzleCatalog catalog,
    AnonymousSessionService anonymousSessions,
    FileGameProgressStore progress,
    CancellationToken cancellationToken) =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var subjectId = anonymousSessions.GetOrCreate(context);
    var completed = await progress.GetCompletedPuzzleIdsAsync(subjectId, cancellationToken);
    var archive = catalog.GetArchive(today)
        .Select(puzzle => new ArchiveItemResponse(
            puzzle.Id,
            puzzle.Number,
            puzzle.PublishDate,
            puzzle.PublishDate == today,
            completed.Contains(puzzle.Id)));
    return Results.Ok(archive);
}).RequireRateLimiting("read");

api.MapGet("/puzzles/{puzzleId}",async (
    string puzzleId,
    HttpContext context,
    PuzzleCatalog catalog,
    AnonymousSessionService anonymousSessions,
    FileGameProgressStore progress,
    CancellationToken cancellationToken) =>
{
    if (!InputGuard.IsSafeId(puzzleId))
    {
        return Results.BadRequest(new { message = "Geçersiz puzzle kimliği." });
    }

    var puzzle = catalog.GetById(puzzleId);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (puzzle is null || puzzle.PublishDate > today)
    {
        return Results.NotFound(new { message = "Puzzle bulunamadı." });
    }

    var subjectId = anonymousSessions.GetOrCreate(context);
    var response = await progress.ExecuteAsync(
        subjectId,
        puzzle.Id,
        session => session.ToPuzzleResponse(puzzle),
        cancellationToken);
    var stats = await progress.GetStatsAsync(subjectId, cancellationToken);
    return Results.Ok(response with { Stats = stats });
}).RequireRateLimiting("read");

api.MapPost("/puzzles/{puzzleId}/reveal",async (
    string puzzleId,
    RevealRequest body,
    HttpContext context,
    PuzzleCatalog catalog,
    AnonymousSessionService anonymousSessions,
    FileGameProgressStore progress,
    CancellationToken cancellationToken) =>
{
    if (context.Request.Headers["X-HS-Request"] != "game")
    {
        return Results.BadRequest(new { message = "Geçersiz istek kaynağı." });
    }

    if (!InputGuard.IsSafeId(puzzleId) || !InputGuard.IsSafeId(body.ClueId, 32))
    {
        return Results.BadRequest(new { message = "Geçersiz istek." });
    }

    var puzzle = catalog.GetById(puzzleId);
    if (puzzle is null || puzzle.PublishDate > DateOnly.FromDateTime(DateTime.UtcNow))
    {
        return Results.NotFound(new { message = "Puzzle bulunamadı." });
    }

    var subjectId = anonymousSessions.GetOrCreate(context);
    var response = await progress.ExecuteAsync(
        subjectId,
        puzzle.Id,
        session =>
        {
            var outcome = session.Reveal(puzzle, body.ClueId);
            return outcome is null
                ? null
                : new RevealResponse(
                    outcome.ClueId,
                    outcome.Value,
                    outcome.CostApplied,
                    session.Score,
                    session.FreeClueAvailable,
                    session.IsComplete,
                    session.IsComplete ? session.BuildResult(puzzle) : null,
                    null);
        },
        cancellationToken);

    if (response is null)
    {
        return Results.BadRequest(new { message = "Bu ipucu açılamadı." });
    }

    var stats = await progress.GetStatsAsync(subjectId, cancellationToken);
    return Results.Ok(response with { Stats = stats });
}).RequireRateLimiting("mutation");

api.MapPost("/puzzles/{puzzleId}/guess",async (
    string puzzleId,
    GuessRequest body,
    HttpContext context,
    PuzzleCatalog catalog,
    AnonymousSessionService anonymousSessions,
    FileGameProgressStore progress,
    CancellationToken cancellationToken) =>
{
    if (context.Request.Headers["X-HS-Request"] != "game")
    {
        return Results.BadRequest(new { message = "Geçersiz istek kaynağı." });
    }

    if (!InputGuard.IsSafeId(puzzleId) || !InputGuard.IsPlayerName(body.PlayerName))
    {
        return Results.BadRequest(new { message = "Geçersiz tahmin." });
    }

    var puzzle = catalog.GetById(puzzleId);
    if (puzzle is null || puzzle.PublishDate > DateOnly.FromDateTime(DateTime.UtcNow))
    {
        return Results.NotFound(new { message = "Puzzle bulunamadı." });
    }

    if (!catalog.IsKnownPlayer(body.PlayerName))
    {
        return Results.BadRequest(new { message = "Futbolcu listede bulunamadı." });
    }

    var subjectId = anonymousSessions.GetOrCreate(context);
    var response = await progress.ExecuteAsync(
        subjectId,
        puzzle.Id,
        session =>
        {
            var outcome = session.Guess(puzzle, body.PlayerName);
            return new GuessResponse(
                outcome.Correct,
                outcome.IsDuplicate,
                session.Score,
                session.AttemptsLeft,
                session.IsComplete,
                session.IsComplete ? session.BuildResult(puzzle) : null,
                null);
        },
        cancellationToken);

    var stats = await progress.GetStatsAsync(subjectId, cancellationToken);
    return Results.Ok(response with { Stats = stats });
}).RequireRateLimiting("mutation");

api.MapGet("/players/search", (string? q, PuzzleCatalog catalog) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
    {
        return Results.Ok(Array.Empty<object>());
    }

    if (!InputGuard.IsSearchQuery(q))
    {
        return Results.BadRequest(new { message = "Geçersiz arama." });
    }

    return Results.Ok(catalog.SearchPlayers(q ?? string.Empty, 8).Select(name => new { name }));
}).RequireRateLimiting("search");

api.MapHealthChecks("/health");

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;

static class SecurityHeaderExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data: https://img.a.transfermarkt.technology https://tmssl.akamaized.net; " +
                "object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next();
        });
}
