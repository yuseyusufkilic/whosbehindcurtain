using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HiddenSeason.Api.Domain;
using Npgsql;

namespace HiddenSeason.Api.Services;

public sealed class FileGameProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly string? _directory;
    private readonly NpgsqlDataSource? _dataSource;
    private readonly SemaphoreSlim _databaseInitialization = new(1, 1);
    private bool _databaseInitialized;

    public FileGameProgressStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var databaseUrl = configuration["DATABASE_URL"]
            ?? configuration.GetConnectionString("Progress");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            _dataSource = NpgsqlDataSource.Create(NormalizeConnectionString(databaseUrl));
            return;
        }

        var configuredPath = configuration["Game:ProgressPath"] ?? "Data/progress";
        _directory = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        Directory.CreateDirectory(_directory);
    }

    public async Task<T> ExecuteAsync<T>(
        string subjectId,
        string puzzleId,
        Func<GameSession, T> operation,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(subjectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var progress = await LoadAsync(subjectId, cancellationToken);
            if (!progress.Games.TryGetValue(puzzleId, out var session))
            {
                session = GameSession.Create(puzzleId);
            }

            var result = operation(session);
            progress.Games[puzzleId] = session;
            await SaveAsync(subjectId, progress, cancellationToken);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlySet<string>> GetCompletedPuzzleIdsAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(subjectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var progress = await LoadAsync(subjectId, cancellationToken);
            return progress.Games
                .Where(entry => entry.Value.IsComplete)
                .Select(entry => entry.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PlayerStatsResponse> GetStatsAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(subjectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var progress = await LoadAsync(subjectId, cancellationToken);
            var completed = progress.Games.Values
                .Where(game => game.IsComplete)
                .OrderByDescending(game => game.UpdatedAt)
                .ToArray();

            return new PlayerStatsResponse(
                completed.Length,
                completed.Length == 0
                    ? 0
                    : (int)Math.Round(completed.Average(game => game.GetAwardedScore())),
                completed.Count(game => game.IsSolved),
                completed.Take(5).Select(game => game.GetAwardedScore()).ToArray());
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PlayerProgress> LoadAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (_dataSource is not null)
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var command = _dataSource.CreateCommand(
                "SELECT payload::text FROM hidden_star_progress WHERE subject_id = $1");
            command.Parameters.AddWithValue(subjectId);
            var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
            return payload is null
                ? new PlayerProgress()
                : JsonSerializer.Deserialize<PlayerProgress>(payload, JsonOptions) ?? new PlayerProgress();
        }

        var path = GetPath(subjectId);
        if (!File.Exists(path))
        {
            return new PlayerProgress();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PlayerProgress>(stream, JsonOptions, cancellationToken)
            ?? new PlayerProgress();
    }

    private async Task SaveAsync(string subjectId, PlayerProgress progress, CancellationToken cancellationToken)
    {
        if (_dataSource is not null)
        {
            await EnsureDatabaseAsync(cancellationToken);
            var payload = JsonSerializer.Serialize(progress, JsonOptions);
            await using var command = _dataSource.CreateCommand("""
                INSERT INTO hidden_star_progress (subject_id, payload, updated_at)
                VALUES ($1, $2::jsonb, now())
                ON CONFLICT (subject_id) DO UPDATE
                SET payload = EXCLUDED.payload, updated_at = now()
                """);
            command.Parameters.AddWithValue(subjectId);
            command.Parameters.AddWithValue(payload);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var path = GetPath(subjectId);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, progress, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, path, true);
    }

    private string GetPath(string subjectId)
    {
        ArgumentNullException.ThrowIfNull(_directory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectId)));
        return Path.Combine(_directory, $"{hash}.json");
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_databaseInitialized || _dataSource is null)
        {
            return;
        }

        await _databaseInitialization.WaitAsync(cancellationToken);
        try
        {
            if (_databaseInitialized)
            {
                return;
            }

            await using var command = _dataSource.CreateCommand("""
                CREATE TABLE IF NOT EXISTS hidden_star_progress (
                    subject_id varchar(64) PRIMARY KEY,
                    payload jsonb NOT NULL,
                    updated_at timestamptz NOT NULL DEFAULT now()
                )
                """);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _databaseInitialized = true;
        }
        finally
        {
            _databaseInitialization.Release();
        }
    }

    private static string NormalizeConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return value;
        }

        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
            SslMode = SslMode.Require,
            MaxPoolSize = 8,
            Timeout = 15,
            CommandTimeout = 10
        }.ConnectionString;
    }

    private sealed class PlayerProgress
    {
        public Dictionary<string, GameSession> Games { get; init; } = new(StringComparer.Ordinal);
    }
}
