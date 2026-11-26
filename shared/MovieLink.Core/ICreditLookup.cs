namespace MovieLink.Core
{
    /// <summary>Answers whether an actor has a qualifying credit in a movie for one dataset version.</summary>
    public interface ICreditLookup
    {
        bool HasCredit(long actorId, long movieId);
    }
}
