#nullable enable
using System;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using MovieLink.Game.Match;
using UnityEngine.UIElements;

namespace MovieLink.Game.UI
{
    /// <summary>Start and target cards, the chain so far, autocomplete search, Back, timer and link counter.</summary>
    public sealed class MatchView
    {
        private const long SearchDebounceMs = 150;
        private readonly MatchController _m;
        private readonly Label _timer, _links, _message, _hint, _startName, _startInitials, _targetName, _targetInitials;
        private readonly ScrollView _chain, _results;
        private readonly TextField _search;
        private IVisualElementScheduledItem? _pendingSearch;
        private int _searchSeq;

        public VisualElement Root { get; }

        public MatchView(MatchController controller)
        {
            _m = controller;
            Root = Ui.Load("Match");
            _timer = Root.Q<Label>("timer");
            _links = Root.Q<Label>("link-counter");
            _message = Root.Q<Label>("message");
            _hint = Root.Q<Label>("search-hint");
            _startName = Root.Q<Label>("start-name");
            _startInitials = Root.Q<Label>("start-initials");
            _targetName = Root.Q<Label>("target-name");
            _targetInitials = Root.Q<Label>("target-initials");
            _chain = Root.Q<ScrollView>("chain");
            _results = Root.Q<ScrollView>("results");
            _search = Root.Q<TextField>("search");

            _search.RegisterValueChangedCallback(_ => ScheduleSearch());
            Root.Q<Button>("back-button").clicked += async () => await _m.UndoAsync();
            Root.Q<Button>("give-up-button").clicked += async () =>
            {
                // A quit mid-game submits a real score (0 if incomplete), not an abort.
                try { await _m.SubmitAsync(); } catch (Net.GameApiException) { }
            };

            _m.Changed += Render;
            _m.Message += ShowError;
            _startName.text = "Loading…";
            Render();
        }

        public void ShowError(string text) => _message.text = text;

        public void Tick()
        {
            double left = _m.SecondsLeft;
            int secs = (int)Math.Ceiling(left);
            _timer.text = $"{secs / 60}:{secs % 60:00}";
            _timer.EnableInClassList("low", left <= 15);
        }

        private void Render()
        {
            var p = _m.Puzzle;
            if (p != null)
            {
                _startName.text = p.StartActor.Name;
                _startInitials.text = Ui.Initials(p.StartActor.Name);
                _targetName.text = p.TargetActor.Name;
                _targetInitials.text = Ui.Initials(p.TargetActor.Name);
            }
            int links = _m.Links;
            _links.text = links == 1 ? "1 link" : $"{links} links";

            _chain.Clear();
            for (int i = 0; i < _m.Chain.Count; i++)
            {
                var n = _m.Chain[i];
                bool hit = p != null && n.Kind == NodeKind.Actor && n.Id == p.TargetActor.Id && i > 0;
                _chain.Add(Ui.ChainRow(n, hit));
            }
            if (_chain.contentContainer.childCount > 0)
                _chain.schedule.Execute(() => _chain.ScrollTo(_chain.contentContainer[_chain.contentContainer.childCount - 1]));

            _hint.text = _m.TimeUp ? "Time's up" : _m.IsComplete ? "Linked!" :
                _m.NextKind == NodeKind.Movie ? "Search a movie this actor was in" : "Search a co-star from this movie";
            _search.SetEnabled(!_m.IsComplete && !_m.TimeUp);
            _search.SetValueWithoutNotify("");
            _results.Clear();
        }

        private void ScheduleSearch()
        {
            _pendingSearch?.Pause();
            _pendingSearch = Root.schedule.Execute(RunSearch).StartingIn(SearchDebounceMs);
        }

        private async void RunSearch()
        {
            int seq = ++_searchSeq;
            string text = _search.value;
            try
            {
                var results = await _m.SearchAsync(text);
                if (seq != _searchSeq) return; // a newer search is on its way
                _results.Clear();
                foreach (var r in results) _results.Add(ResultRow(r));
            }
            catch (Net.GameApiException e) { ShowError(e.Message); }
        }

        private VisualElement ResultRow(NodeDto node)
        {
            var row = new Button(async () =>
            {
                _message.text = "";
                await _m.PickAsync(node);
            });
            row.AddToClassList("result-row");
            row.text = "";
            row.Add(Ui.Text(node.Name));
            if (!string.IsNullOrEmpty(node.Subtitle)) row.Add(Ui.Text(node.Subtitle!, "subtitle"));
            return row;
        }
    }
}
