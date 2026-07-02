using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HiddenSeason.Api.Domain;

namespace HiddenSeason.Api.Services;

public sealed class FileGameProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly string _directory;

    public FileGameProgressStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
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
                    : (int)Math.Round(completed.Average(game => game.Score)),
                completed.Count(game => game.IsSolved),
                completed.Take(5).Select(game => game.Score).ToArray());
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PlayerProgress> LoadAsync(string subjectId, CancellationToken cancellationToken)
    {
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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectId)));
        return Path.Combine(_directory, $"{hash}.json");
    }

    private sealed class PlayerProgress
    {
        public Dictionary<string, GameSession> Games { get; init; } = new(StringComparer.Ordinal);
    }
}
