namespace HiddenSeason.Api.Domain;

public sealed class GameSession
{
    public string PuzzleId { get; init; } = string.Empty;
    public int Score { get; set; } = 100;
    public int AttemptsLeft { get; set; } = 3;
    public bool IsComplete { get; set; }
    public bool IsSolved { get; set; }
    public bool FreeClueUsed { get; set; }
    public HashSet<string> RevealedClues { get; init; } = [];
    public List<string> Guesses { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool FreeClueAvailable => !FreeClueUsed;

    public static GameSession Create(string puzzleId) => new() { PuzzleId = puzzleId };

    public RevealOutcome? Reveal(DailyPuzzle puzzle, string clueId)
    {
        if (IsComplete || puzzle.Id != PuzzleId)
        {
            return null;
        }

        var clue = puzzle.Clues.FirstOrDefault(item => item.Id == clueId);
        if (clue is null)
        {
            return null;
        }

        if (RevealedClues.Contains(clue.Id))
        {
            return new RevealOutcome(clue.Id, clue.Value, 0);
        }

        var isFreeReveal = FreeClueAvailable && clue.IsFreeEligible;
        var costApplied = isFreeReveal ? 0 : clue.Cost;

        RevealedClues.Add(clue.Id);
        if (isFreeReveal)
        {
            FreeClueUsed = true;
        }

        Score = Math.Max(0, Score - costApplied);
        UpdatedAt = DateTimeOffset.UtcNow;

        return new RevealOutcome(clue.Id, clue.Value, costApplied);
    }

    public GuessOutcome Guess(DailyPuzzle puzzle, string playerName)
    {
        if (IsComplete || puzzle.Id != PuzzleId)
        {
            return new GuessOutcome(IsSolved);
        }

        Guesses.Add(playerName.Trim());
        IsSolved = TextNormalizer.Normalize(playerName) == TextNormalizer.Normalize(puzzle.Answer);

        if (!IsSolved)
        {
            AttemptsLeft--;
            Score = Math.Max(0, Score - 10);
        }

        IsComplete = IsSolved || AttemptsLeft == 0;
        UpdatedAt = DateTimeOffset.UtcNow;
        return new GuessOutcome(IsSolved);
    }

    public PuzzleResponse ToPuzzleResponse(DailyPuzzle puzzle) => new(
        puzzle.Id,
        puzzle.Number,
        puzzle.PublishDate,
        Score,
        AttemptsLeft,
        IsComplete,
        IsSolved,
        FreeClueAvailable,
        Guesses,
        puzzle.Clues.Select(clue =>
        {
            var isRevealed = RevealedClues.Contains(clue.Id);
            return new ClueResponse(
                clue.Id,
                clue.Label,
                clue.Cost,
                FreeClueAvailable && clue.IsFreeEligible ? 0 : clue.Cost,
                clue.Icon,
                clue.IsFreeEligible,
                isRevealed,
                isRevealed ? clue.Value : null);
        }),
        IsComplete ? BuildResult(puzzle) : null);

    public GameResult BuildResult(DailyPuzzle puzzle) => new(
        puzzle.Answer,
        puzzle.SeasonLabel,
        puzzle.PhotoUrl,
        puzzle.ClubLogoUrl,
        IsSolved);
}
