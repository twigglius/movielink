using System.Security.Cryptography;
using System.Text;

namespace MovieLink.Api;

/// <summary>
/// Signed per-match token: HMAC-SHA256 over match id, player id, puzzle id and start time.
/// Ties every step to one match and player, so steps can't be replayed or moved to another match.
/// </summary>
public sealed class MatchTokens
{
    private readonly byte[] _key;

    public MatchTokens(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length < 32) throw new ArgumentException("Match token key must be at least 32 bytes.");
    }

    public string Issue(Guid matchId, string playerId, long puzzleId, DateTime startedAtUtc)
    {
        string payload = $"{matchId:N}.{startedAtUtc.Ticks}";
        return payload + "." + Sign(matchId, playerId, puzzleId, startedAtUtc);
    }

    /// <summary>Checks the token against the stored match. Constant-time compare.</summary>
    public bool Verify(string? token, Guid matchId, string playerId, long puzzleId, DateTime startedAtUtc)
    {
        if (string.IsNullOrEmpty(token)) return false;
        string expected = Issue(matchId, playerId, puzzleId, startedAtUtc);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(expected));
    }

    private string Sign(Guid matchId, string playerId, long puzzleId, DateTime startedAtUtc)
    {
        string msg = $"{matchId:N}|{playerId}|{puzzleId}|{startedAtUtc.Ticks}";
        using var h = new HMACSHA256(_key);
        return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(msg)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
