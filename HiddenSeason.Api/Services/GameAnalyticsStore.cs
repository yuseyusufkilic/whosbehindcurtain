using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace HiddenSeason.Api.Services;

public sealed class GameAnalyticsStore
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<GameAnalyticsStore> _logger;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _initialized;

    public GameAnalyticsStore(IConfiguration configuration, ILogger<GameAnalyticsStore> logger)
    {
        _logger = logger;
        var databaseUrl = configuration["DATABASE_URL"]
            ?? configuration.GetConnectionString("Progress");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            _dataSource = NpgsqlDataSource.Create(NormalizeConnectionString(databaseUrl));
        }
    }

    public async Task TrackAsync(
        string subjectId,
        string eventType,
        string puzzleId,
        string eventKey,
        int score,
        int attemptsLeft,
        bool? solved = null,
        CancellationToken cancellationToken = default)
    {
        if (_dataSource is null)
        {
            return;
        }

        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var command = _dataSource.CreateCommand("""
                INSERT INTO hidden_star_events
                    (subject_hash, event_type, puzzle_id, event_key, score, attempts_left, solved)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (subject_hash, event_type, puzzle_id, event_key) DO NOTHING
                """);
            command.Parameters.AddWithValue(HashSubject(subjectId));
            command.Parameters.AddWithValue(eventType);
            command.Parameters.AddWithValue(puzzleId);
            command.Parameters.AddWithValue(eventKey);
            command.Parameters.AddWithValue(score);
            command.Parameters.AddWithValue(attemptsLeft);
            command.Parameters.AddWithValue(solved is null ? DBNull.Value : solved.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Analytics event could not be persisted: {EventType}", eventType);
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_initialized || _dataSource is null)
        {
            return;
        }

        await _initialization.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var command = _dataSource.CreateCommand("""
                CREATE TABLE IF NOT EXISTS hidden_star_events (
                    id bigserial PRIMARY KEY,
                    subject_hash char(64) NOT NULL,
                    event_type varchar(32) NOT NULL,
                    puzzle_id varchar(160) NOT NULL,
                    event_key varchar(80) NOT NULL,
                    score smallint NOT NULL,
                    attempts_left smallint NOT NULL,
                    solved boolean NULL,
                    occurred_at timestamptz NOT NULL DEFAULT now(),
                    UNIQUE (subject_hash, event_type, puzzle_id, event_key)
                );
                CREATE INDEX IF NOT EXISTS ix_hidden_star_events_occurred_at
                    ON hidden_star_events (occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_hidden_star_events_puzzle
                    ON hidden_star_events (puzzle_id, event_type);
                """);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initialization.Release();
        }
    }

    private static string HashSubject(string subjectId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectId)));

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
}
