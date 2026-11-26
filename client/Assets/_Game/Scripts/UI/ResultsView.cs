#nullable enable
using System;
using MovieLink.Core.Dtos;
using UnityEngine.UIElements;

namespace MovieLink.Game.UI
{
    /// <summary>Score, time, the player's chain next to the shortest known chain, then Continue.</summary>
    public sealed class ResultsView
    {
        public VisualElement Root { get; }
        public event Action? ContinueClicked;

        public ResultsView(SubmitResponse r)
        {
            Root = Ui.Load("Results");
            Root.Q<Label>("headline").text = r.IsComplete
                ? (r.Links <= r.OptimalLinks ? "Perfect link!" : "Linked!")
                : "Out of time";
            Root.Q<Label>("score").text = r.Score.ToString("N0");
            Root.Q<Label>("summary").text = r.IsComplete
                ? $"{r.Links} {(r.Links == 1 ? "link" : "links")} in {r.SecondsTaken:0.0} s. Best possible: {r.OptimalLinks}."
                : $"No complete chain. Best possible was {r.OptimalLinks} links.";

            var yours = Root.Q<VisualElement>("your-chain");
            for (int i = 0; i < r.Chain.Count; i++)
                yours.Add(Ui.ChainRow(r.Chain[i], r.IsComplete && i == r.Chain.Count - 1));
            var best = Root.Q<VisualElement>("best-chain");
            foreach (var n in r.ShortestKnownChain) best.Add(Ui.ChainRow(n));

            Root.Q<Button>("continue-button").clicked += () => ContinueClicked?.Invoke();
        }
    }
}
