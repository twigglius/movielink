#nullable enable
using System;

namespace MovieLink.Core
{
    public enum NodeKind
    {
        Actor = 0,
        Movie = 1,
    }

    /// <summary>One position in a chain: an actor or a movie, by database id.</summary>
    public readonly struct NodeRef : IEquatable<NodeRef>
    {
        public NodeRef(NodeKind kind, long id)
        {
            Kind = kind;
            Id = id;
        }

        public NodeKind Kind { get; }
        public long Id { get; }

        public static NodeRef Actor(long id) => new NodeRef(NodeKind.Actor, id);
        public static NodeRef Movie(long id) => new NodeRef(NodeKind.Movie, id);

        public bool Equals(NodeRef other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object? obj) => obj is NodeRef other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Id.GetHashCode();
        public override string ToString() => (Kind == NodeKind.Actor ? "A" : "M") + Id;
    }
}
