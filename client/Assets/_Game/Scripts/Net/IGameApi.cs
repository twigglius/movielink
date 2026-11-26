#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieLink.Core;
using MovieLink.Core.Dtos;

namespace MovieLink.Game.Net
{
    /// <summary>The game server, as the client sees it. HttpGameApi talks to the real server; FakeGameApi runs offline.</summary>
    public interface IGameApi
    {
        Task<CreateMatchResponse> CreateMatchAsync(CreateMatchRequest request);
        Task<List<NodeDto>> SearchAsync(Guid matchId, string token, NodeKind kind, string query);
        Task<ChainStateResponse> StepAsync(Guid matchId, string token, NodeKind kind, long id);
        Task<ChainStateResponse> UndoAsync(Guid matchId, string token);
        Task<SubmitResponse> SubmitAsync(Guid matchId, string token);
    }

    public sealed class GameApiException : Exception
    {
        public GameApiException(string message, long status = 0, bool isNetworkError = false) : base(message)
        {
            Status = status;
            IsNetworkError = isNetworkError;
        }
        public long Status { get; }
        public bool IsNetworkError { get; }
    }
}
