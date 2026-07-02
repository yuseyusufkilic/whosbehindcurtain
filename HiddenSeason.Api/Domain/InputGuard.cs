namespace HiddenSeason.Api.Domain;

public static class InputGuard
{
    public static bool IsSafeId(string? value, int maxLength = 80) =>
        IsPlainText(value, 1, maxLength) && value!.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static bool IsPlayerName(string? value) => IsPlainText(value, 2, 80);

    public static bool IsSearchQuery(string? value) => IsPlainText(value, 2, 64);

    private static bool IsPlainText(string? value, int minimumLength, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= minimumLength
            && trimmed.Length <= maximumLength
            && !trimmed.Any(char.IsControl);
    }
}
