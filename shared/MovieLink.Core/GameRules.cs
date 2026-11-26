namespace MovieLink.Core
{
    /// <summary>Tunable match rules. Defaults follow the build plan; playtests set the final values.</summary>
    public sealed class GameRules
    {
        public const int DefaultMaxLinks = 6;
        public const int DefaultTimeLimitSeconds = 120;

        public GameRules(int maxLinks = DefaultMaxLinks, int timeLimitSeconds = DefaultTimeLimitSeconds)
        {
            if (maxLinks < 1) throw new System.ArgumentOutOfRangeException(nameof(maxLinks));
            if (timeLimitSeconds < 1) throw new System.ArgumentOutOfRangeException(nameof(timeLimitSeconds));
            MaxLinks = maxLinks;
            TimeLimitSeconds = timeLimitSeconds;
        }

        /// <summary>Most movie steps a chain may use.</summary>
        public int MaxLinks { get; }

        /// <summary>Round timer in seconds.</summary>
        public int TimeLimitSeconds { get; }

        public static GameRules Default { get; } = new GameRules();
    }
}
