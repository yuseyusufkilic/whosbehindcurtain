namespace HiddenSeason.Api.Domain;

public sealed record ClueDefinition(string Id, string Label, string Value, int Cost, string Icon)
{
    public bool IsFreeEligible => Cost <= 6;
}

public sealed record DailyPuzzle(
    string Id,
    string Number,
    DateOnly PublishDate,
    string Answer,
    string SeasonLabel,
    IReadOnlyList<ClueDefinition> Clues,
    string PhotoUrl,
    string ClubLogoUrl);

public sealed record PuzzleCatalogDocument(
    IReadOnlyList<string> CandidatePlayers,
    IReadOnlyList<DailyPuzzle> Puzzles);

public sealed record RevealRequest(string ClueId);
public sealed record GuessRequest(string PlayerName);

public sealed record ClueResponse(
    string Id,
    string Label,
    int Cost,
    int EffectiveCost,
    string Icon,
    bool IsFreeEligible,
    bool IsRevealed,
    string? Value);

public sealed record PuzzleResponse(
    string PuzzleId,
    string Number,
    DateOnly PublishDate,
    int Score,
    int AttemptsLeft,
    bool IsComplete,
    bool IsSolved,
    bool FreeClueAvailable,
    IReadOnlyList<string> Guesses,
    IEnumerable<ClueResponse> Clues,
    GameResult? Result);

public sealed record ArchiveItemResponse(
    string PuzzleId,
    string Number,
    DateOnly PublishDate,
    bool IsToday,
    bool IsComplete);

public sealed record RevealResponse(
    string ClueId,
    string Value,
    int CostApplied,
    int Score,
    bool FreeClueAvailable);

public sealed record GuessResponse(
    bool Correct,
    int Score,
    int AttemptsLeft,
    bool IsComplete,
    GameResult? Result);

public sealed record RevealOutcome(string ClueId, string Value, int CostApplied);
public sealed record GuessOutcome(bool Correct);
public sealed record GameResult(
    string PlayerName,
    string Season,
    string PhotoUrl,
    string ClubLogoUrl,
    bool Solved);
