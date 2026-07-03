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
        IsComplete = Score == 0;
        UpdatedAt = DateTimeOffset.UtcNow;

        return new RevealOutcome(clue.Id, clue.Value, costApplied);
    }

    public GuessOutcome Guess(DailyPuzzle puzzle, string playerName)
    {
        if (IsComplete || puzzle.Id != PuzzleId)
        {
            return new GuessOutcome(IsSolved);
        }

        var trimmedPlayerName = playerName.Trim();
        var normalizedPlayerName = TextNormalizer.Normalize(trimmedPlayerName);
        if (Guesses.Any(previous => TextNormalizer.Normalize(previous) == normalizedPlayerName))
        {
            return new GuessOutcome(false, true);
        }

        Guesses.Add(trimmedPlayerName);
        IsSolved = normalizedPlayerName == TextNormalizer.Normalize(puzzle.Answer);

        if (!IsSolved)
        {
            AttemptsLeft--;
            Score = Math.Max(0, Score - 10);
            if (AttemptsLeft == 0)
            {
                Score = 0;
            }
        }

        IsComplete = IsSolved || AttemptsLeft == 0 || Score == 0;
        UpdatedAt = DateTimeOffset.UtcNow;
        return new GuessOutcome(IsSolved);
    }

    public PuzzleResponse ToPuzzleResponse(DailyPuzzle puzzle) => new(
        puzzle.Id,
        puzzle.Number,
        puzzle.PublishDate,
        puzzle.SeasonLabel,
        GetAwardedScore(),
        AttemptsLeft,
        IsComplete,
        IsSolved,
        FreeClueAvailable,
        Guesses,
        puzzle.Clues.Where(clue => clue.Id != "season").Select(clue =>
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
        IsComplete ? BuildResult(puzzle) : null,
        null);

    public int GetAwardedScore() => IsComplete && !IsSolved ? 0 : Score;

    public GameResult BuildResult(DailyPuzzle puzzle) => new(
        puzzle.Answer,
        puzzle.SeasonLabel,
        puzzle.PhotoUrl,
        puzzle.ClubLogoUrl,
        IsSolved);
}
