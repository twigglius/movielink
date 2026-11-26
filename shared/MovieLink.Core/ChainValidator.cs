#nullable enable
using System;
using System.Collections.Generic;

namespace MovieLink.Core
{
    public enum StepError
    {
        None = 0,
        /// <summary>The chain must alternate actor, movie, actor.</summary>
        WrongKind,
        /// <summary>The actor has no qualifying credit in the movie.</summary>
        NotInMovie,
        /// <summary>That actor or movie is already in the chain.</summary>
        Repeated,
        /// <summary>Adding this step would go past the link limit.</summary>
        TooManyLinks,
        /// <summary>The chain already reached the target.</summary>
        AlreadyComplete,
        /// <summary>The chain does not begin with the puzzle's start actor.</summary>
        WrongStart,
    }

    public sealed class ChainResult
    {
        public ChainResult(StepError error, int errorIndex, int links, bool isComplete)
        {
            Error = error;
            ErrorIndex = errorIndex;
            Links = links;
            IsComplete = isComplete;
        }

        public StepError Error { get; }
        /// <summary>Index of the first bad node, or -1.</summary>
        public int ErrorIndex { get; }
        /// <summary>Movie steps in the valid prefix.</summary>
        public int Links { get; }
        public bool IsComplete { get; }
        public bool IsValid => Error == StepError.None;
    }

    /// <summary>
    /// Chain rules shared by client (instant feedback) and server (authority).
    /// A chain is: start actor, movie, actor, movie, ... and is complete when it ends on the target actor.
    /// </summary>
    public static class ChainValidator
    {
        /// <summary>Checks whether <paramref name="next"/> may be appended to a chain that is already valid.</summary>
        public static StepError ValidateStep(
            long startActorId,
            long targetActorId,
            IReadOnlyList<NodeRef> chain,
            NodeRef next,
            ICreditLookup credits,
            GameRules rules)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            if (credits == null) throw new ArgumentNullException(nameof(credits));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            if (chain.Count == 0)
            {
                return next.Kind == NodeKind.Actor && next.Id == startActorId ? StepError.None : StepError.WrongStart;
            }

            NodeRef last = chain[chain.Count - 1];
            if (last.Kind == NodeKind.Actor && last.Id == targetActorId && chain.Count > 1)
                return StepError.AlreadyComplete;
            if (next.Kind == last.Kind)
                return StepError.WrongKind;

            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i].Equals(next)) return StepError.Repeated;
            }

            int links = CountMovies(chain);
            if (next.Kind == NodeKind.Movie)
            {
                if (links >= rules.MaxLinks) return StepError.TooManyLinks;
                return credits.HasCredit(last.Id, next.Id) ? StepError.None : StepError.NotInMovie;
            }

            // next is an actor, last is a movie.
            if (!credits.HasCredit(next.Id, last.Id)) return StepError.NotInMovie;
            // At the link limit, the only actor still worth adding is the target.
            if (links >= rules.MaxLinks && next.Id != targetActorId) return StepError.TooManyLinks;
            return StepError.None;
        }

        /// <summary>Re-checks a whole chain from scratch. Used by the server on submit.</summary>
        public static ChainResult ValidateChain(
            long startActorId,
            long targetActorId,
            IReadOnlyList<NodeRef> chain,
            ICreditLookup credits,
            GameRules rules)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            var prefix = new List<NodeRef>(chain.Count);
            int links = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                StepError err = ValidateStep(startActorId, targetActorId, prefix, chain[i], credits, rules);
                if (err != StepError.None)
                    return new ChainResult(err, i, links, false);
                prefix.Add(chain[i]);
                if (chain[i].Kind == NodeKind.Movie) links++;
            }

            bool complete = IsComplete(targetActorId, prefix);
            return new ChainResult(StepError.None, -1, links, complete);
        }

        public static bool IsComplete(long targetActorId, IReadOnlyList<NodeRef> chain) =>
            chain.Count >= 3
            && chain[chain.Count - 1].Kind == NodeKind.Actor
            && chain[chain.Count - 1].Id == targetActorId;

        public static int CountMovies(IReadOnlyList<NodeRef> chain)
        {
            int n = 0;
            for (int i = 0; i < chain.Count; i++)
                if (chain[i].Kind == NodeKind.Movie) n++;
            return n;
        }

        /// <summary>Short player-facing text for a step error.</summary>
        public static string Describe(StepError error)
        {
            switch (error)
            {
                case StepError.None: return "OK";
                case StepError.WrongKind: return "Pick a movie after an actor, and an actor after a movie.";
                case StepError.NotInMovie: return "That actor isn't credited in that movie.";
                case StepError.Repeated: return "That's already in your chain.";
                case StepError.TooManyLinks: return "That would go past the link limit.";
                case StepError.AlreadyComplete: return "You've already reached the target.";
                case StepError.WrongStart: return "The chain must start with the start actor.";
                default: return error.ToString();
            }
        }
    }
}
