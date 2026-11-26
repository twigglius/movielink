using System;

namespace MovieLink.Core
{
    /// <summary>
    /// Tiered score: fewer links always beats any time advantage.
    /// score = 1000 * (Lmax + 1 - L) + floor(999 * (T - t) / T), and 0 for no complete chain or t >= T.
    /// </summary>
    public static class Scoring
    {
        public const int TierSize = 1000;
        public const int MaxTimeBonus = 999;

        public static int Compute(int links, double secondsTaken, GameRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (links < 1 || links > rules.MaxLinks) return 0;
            if (double.IsNaN(secondsTaken) || secondsTaken >= rules.TimeLimitSeconds) return 0;

            double t = Math.Max(0.0, secondsTaken);
            int linkScore = TierSize * (rules.MaxLinks + 1 - links);
            int timeBonus = (int)Math.Floor(MaxTimeBonus * (rules.TimeLimitSeconds - t) / rules.TimeLimitSeconds);
            // Guard the tier boundary against floating point drift.
            timeBonus = Math.Min(MaxTimeBonus, Math.Max(0, timeBonus));
            return linkScore + timeBonus;
        }

        /// <summary>Highest score possible for a given link count (finishing at t = 0).</summary>
        public static int MaxFor(int links, GameRules rules) => Compute(links, 0, rules);
    }
}
