using System.Security.Cryptography;
using System.Text;

namespace HiddenSeason.Api.Services;

public sealed class AnonymousSessionService
{
    private const string CookieName = "hs_session";
    private readonly byte[] _signingKey;

    public AnonymousSessionService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredKey = configuration["SESSION_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "SESSION_SIGNING_KEY production ortamında tanımlanmalıdır.");
            }

            configuredKey = "hidden-star-local-development-key-change-me";
        }

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
    }

    public string GetOrCreate(HttpContext context)
    {
        var cookieValue = context.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(cookieValue))
        {
            var parts = cookieValue.Split('.', 2);
            if (parts.Length == 2
                && Guid.TryParseExact(parts[0], "N", out _)
                && IsValidSignature(parts[0], parts[1]))
            {
                return parts[0];
            }
        }

        var created = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, $"{created}.{Sign(created)}", new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true
        });
        return created;
    }

    private string Sign(string value) =>
        Convert.ToBase64String(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(value)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private bool IsValidSignature(string value, string signature)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(value));
        var supplied = Encoding.ASCII.GetBytes(signature);
        return expected.Length == supplied.Length
            && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
