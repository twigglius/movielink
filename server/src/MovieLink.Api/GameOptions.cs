namespace MovieLink.Api;

public sealed class GameOptions
{
    public int MaxLinks { get; set; } = MovieLink.Core.GameRules.DefaultMaxLinks;
    public int TimeLimitSeconds { get; set; } = MovieLink.Core.GameRules.DefaultTimeLimitSeconds;
    /// <summary>Time credited back per server request, so slow networks aren't punished.</summary>
    public int LatencyAllowanceMsPerRequest { get; set; } = 150;
    /// <summary>Cap on the total latency allowance per match.</summary>
    public int MaxLatencyAllowanceMs { get; set; } = 3000;
    /// <summary>Base64 HMAC key (32+ bytes). Set via config or MOVIELINK_Game__TokenKey in production.</summary>
    public string TokenKey { get; set; } = "";
    /// <summary>Origins allowed to call the API from a browser (the dev WebGL build).</summary>
    public string[] CorsOrigins { get; set; } = Array.Empty<string>();
}
