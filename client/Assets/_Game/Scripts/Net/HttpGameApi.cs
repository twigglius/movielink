#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using MovieLink.Game.App;
using UnityEngine.Networking;

namespace MovieLink.Game.Net
{
    /// <summary>UnityWebRequest client for the ASP.NET Core server. Works in WebGL and on iOS.</summary>
    public sealed class HttpGameApi : IGameApi
    {
        private const string TokenHeader = "X-Match-Token";
        private readonly string _base;

        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            NullValueHandling = NullValueHandling.Ignore,
        };

        public HttpGameApi(string baseUrl) => _base = baseUrl.TrimEnd('/');

        public Task<CreateMatchResponse> CreateMatchAsync(CreateMatchRequest request) =>
            SendAsync<CreateMatchResponse>("POST", "/api/matches", null, request);

        public async Task<List<NodeDto>> SearchAsync(Guid matchId, string token, NodeKind kind, string query)
        {
            string k = kind == NodeKind.Actor ? "actor" : "movie";
            var r = await SendAsync<SearchResponse>("GET",
                $"/api/matches/{matchId}/search?kind={k}&q={UnityWebRequest.EscapeURL(query)}", token, null);
            return r.Results;
        }

        public Task<ChainStateResponse> StepAsync(Guid matchId, string token, NodeKind kind, long id) =>
            SendAsync<ChainStateResponse>("POST", $"/api/matches/{matchId}/steps", token, new StepRequest { Kind = kind, Id = id });

        public Task<ChainStateResponse> UndoAsync(Guid matchId, string token) =>
            SendAsync<ChainStateResponse>("POST", $"/api/matches/{matchId}/undo", token, null);

        /// <summary>Submit is idempotent on the server, so it is safe to retry after a dropped connection.</summary>
        public async Task<SubmitResponse> SubmitAsync(Guid matchId, string token)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return await SendAsync<SubmitResponse>("POST", $"/api/matches/{matchId}/submit", token, null); }
                catch (GameApiException e) when (e.IsNetworkError && attempt < 4)
                {
                    await MainThreadDelay.Seconds(0.5f * attempt);
                }
            }
        }

        private async Task<T> SendAsync<T>(string method, string path, string? token, object? body)
        {
            using var req = new UnityWebRequest(_base + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 15 };
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, Json)));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (token != null) req.SetRequestHeader(TokenHeader, token);

            await req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError)
                throw new GameApiException("Can't reach the game server. Check your connection.", 0, isNetworkError: true);
            if (req.result != UnityWebRequest.Result.Success)
                throw new GameApiException($"Server error {req.responseCode}: {req.downloadHandler.text}", req.responseCode);
            return JsonConvert.DeserializeObject<T>(req.downloadHandler.text, Json)
                ?? throw new GameApiException("Empty response from server.", req.responseCode);
        }
    }

    /// <summary>Lets UnityWebRequest be awaited on the main thread (also in WebGL, which has no threads).</summary>
    public static class UnityWebRequestAwaiter
    {
        public static TaskAwaiter<UnityWebRequest.Result> GetAwaiter(this UnityWebRequestAsyncOperation op)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest.Result>();
            if (op.isDone) tcs.SetResult(op.webRequest.result);
            else op.completed += _ => tcs.SetResult(op.webRequest.result);
            return tcs.Task.GetAwaiter();
        }
    }
}
