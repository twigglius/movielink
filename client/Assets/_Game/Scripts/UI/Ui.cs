#nullable enable
using System;
using System.Linq;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using UnityEngine;
using UnityEngine.UIElements;

namespace MovieLink.Game.UI
{
    public static class Ui
    {
        public static VisualElement Load(string name)
        {
            var tree = Resources.Load<VisualTreeAsset>("UI/" + name);
            if (tree == null) throw new InvalidOperationException($"Missing Resources/UI/{name}.uxml");
            var container = tree.Instantiate();
            container.style.flexGrow = 1;
            return container;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        /// <summary>Initials stand in for actor photos at launch (no likeness rights needed).</summary>
        public static string Initials(string name)
        {
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => char.IsLetter(p[0])).ToArray();
            if (parts.Length == 0) return "?";
            string first = parts[0].Substring(0, 1);
            string last = parts.Length > 1 ? parts[parts.Length - 1].Substring(0, 1) : "";
            return (first + last).ToUpperInvariant();
        }

        public static VisualElement ChainRow(NodeDto node, bool isTarget = false)
        {
            var row = new VisualElement();
            row.AddToClassList("chain-node");
            row.AddToClassList(node.Kind == NodeKind.Movie ? "movie" : "actor");
            if (isTarget) row.AddToClassList("target-hit");
            row.Add(Text(node.Name));
            if (!string.IsNullOrEmpty(node.Subtitle)) row.Add(Text(node.Subtitle!, "subtitle"));
            return row;
        }

        /// <summary>A modal panel over the current screen. Tap outside or Close to dismiss.</summary>
        public static VisualElement Overlay(string title, Action<VisualElement> fill)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.Add(Text(title, "panel-title"));
            fill(panel);
            var close = new Button(() => overlay.RemoveFromHierarchy()) { text = "Close" };
            close.AddToClassList("secondary-button");
            close.style.marginTop = 24;
            panel.Add(close);
            overlay.Add(panel);
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) overlay.RemoveFromHierarchy(); });
            return overlay;
        }
    }
}
