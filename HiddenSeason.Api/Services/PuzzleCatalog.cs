using System.Text.Json;
using HiddenSeason.Api.Domain;

namespace HiddenSeason.Api.Services;

public sealed class PuzzleCatalog
{
    private readonly PuzzleCatalogDocument _document;
    private readonly Dictionary<string, DailyPuzzle> _puzzlesById;
    private readonly IReadOnlyList<(string Name, string Normalized)> _players;
    private readonly IReadOnlyList<DailyPuzzle> _puzzlesByDate;

    private PuzzleCatalog(PuzzleCatalogDocument document)
    {
        if (document.Puzzles.Count == 0)
        {
            throw new InvalidOperationException("Puzzle kataloğunda en az bir kayıt bulunmalıdır.");
        }

        _document = document;
        _puzzlesById = document.Puzzles.ToDictionary(puzzle => puzzle.Id, StringComparer.Ordinal);
        _puzzlesByDate = document.Puzzles.OrderBy(puzzle => puzzle.PublishDate).ToArray();
        _players = document.CandidatePlayers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (name, TextNormalizer.Normalize(name)))
            .ToArray();

        if (_puzzlesById.Count != document.Puzzles.Count)
        {
            throw new InvalidOperationException("Puzzle kimlikleri benzersiz olmalıdır.");
        }
    }

    public static PuzzleCatalog Load(string contentRoot, string relativePath)
    {
        var path = Path.GetFullPath(relativePath, contentRoot);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Puzzle kataloğu bulunamadı.", path);
        }

        var document = JsonSerializer.Deserialize<PuzzleCatalogDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return new PuzzleCatalog(document ?? throw new InvalidOperationException("Puzzle kataloğu okunamadı."));
    }

    public DailyPuzzle GetDaily(DateOnly date) =>
        _puzzlesByDate.LastOrDefault(puzzle => puzzle.PublishDate <= date)
        ?? _puzzlesByDate[0];

    public DailyPuzzle? GetById(string id) => _puzzlesById.GetValueOrDefault(id);

    public IReadOnlyList<DailyPuzzle> GetArchive(DateOnly date) =>
        _puzzlesByDate.Where(puzzle => puzzle.PublishDate <= date).ToArray();

    public bool IsKnownPlayer(string playerName)
    {
        var normalized = TextNormalizer.Normalize(playerName);
        return _players.Any(player => player.Normalized == normalized);
    }

    public IEnumerable<string> SearchPlayers(string query, int limit)
    {
        var normalized = TextNormalizer.Normalize(query);
        if (normalized.Length < 2)
        {
            return [];
        }

        return _players
            .Where(player => player.Normalized.Contains(normalized, StringComparison.Ordinal))
            .Take(limit)
            .Select(player => player.Name);
    }
}
