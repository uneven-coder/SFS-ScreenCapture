using System;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using Button = SFS.UI.ModGUI.Button;

namespace FrameEmbededState.Lib
{
    public static class UiHelper
    {
        public static class Metrics
        {   // Centralized UI measurement constants
            public const int Padding = 2;
            public const int Spacing = 5;
            public const int ButtonHeight = 42;
            public const int LabelHeight = 42;
            public const int InputHeight = 42;
            public const int FoldoutButtonSize = 30;
            public const int MinFieldWidth = 80;
            public const int DefaultColumns = 2;
        }

        public static class Layout
        {   // Standardized layout configurations
            public static readonly Color HighlightTint = new Color(0.2f, 0.2f, 0.45f, 0f);
            public static readonly RectOffset StandardPadding = new RectOffset(Metrics.Padding, Metrics.Padding, Metrics.Padding, Metrics.Padding);
            public static readonly RectOffset NoPadding = new RectOffset(0, 0, 0, 0);
        }

        public static Window CreateScrollableWindow(Transform parent, int width, int height, string title = "", float opacity = 0.15f, bool vertical = true, bool horizontal = false)
        {   // Create window with layout and optional scrolling in specified directions
            var window = Builder.CreateWindow(parent, Builder.GetRandomID(), width, height, 0, 0, false, false, opacity, title);
            if (string.IsNullOrEmpty(title)) window.TitleOpacity = 0f;
            window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, Metrics.Spacing, Layout.StandardPadding, true);
            if (vertical || horizontal) window.EnableScrolling(vertical ? SFS.UI.ModGUI.Type.Vertical : SFS.UI.ModGUI.Type.Horizontal);
            return window;
        }

        public static RectTransform CreateVerticalContainer(Transform parent, int spacing = -1, RectOffset padding = null) =>
            CreateContainer(parent, SFS.UI.ModGUI.Type.Vertical, spacing, padding);

        public static RectTransform CreateHorizontalContainer(Transform parent, int spacing = -1, RectOffset padding = null) =>
            CreateContainer(parent, SFS.UI.ModGUI.Type.Horizontal, spacing, padding);

        private static RectTransform CreateContainer(Transform parent, SFS.UI.ModGUI.Type layout, int spacing = -1, RectOffset padding = null)
        {   // Create container with specified layout direction and spacing
            var container = Builder.CreateContainer(parent, 0, 0);
            container.CreateLayoutGroup(layout, TextAnchor.MiddleLeft, spacing < 0 ? Metrics.Spacing : spacing, padding ?? Layout.StandardPadding, true);
            return container.rectTransform;
        }

        public static RectTransform CreateLabel(Transform parent, int width, string text, int height = -1) =>
            Builder.CreateLabel(parent, width, height < 0 ? Metrics.LabelHeight : height, 0, 0, text).rectTransform;

        public static Button CreateButton(Transform parent, int width, string text, Action onClick, int height = -1) =>
            Builder.CreateButton(parent, width, height < 0 ? Metrics.ButtonHeight : height, 0, 0, onClick, text);

        public static TextInput CreateTextInput(Transform parent, int width, string initialText, UnityEngine.Events.UnityAction<string> onValueChanged, int height = -1) =>
            Builder.CreateTextInput(parent, width, height < 0 ? Metrics.InputHeight : height, 0, 0, initialText, onValueChanged);

        public static Button CreateFoldoutButton(Transform parent, bool isExpanded, Action onClick) =>
            Builder.CreateButton(parent, Metrics.FoldoutButtonSize, Metrics.FoldoutButtonSize, 0, 0, onClick, isExpanded ? "▼" : "►");

        public static void HighlightButton(Graphic buttonGraphic, Color normalColor, bool isHighlighted)
        {   // Apply or remove highlight tint from button graphic
            if (buttonGraphic != null) buttonGraphic.color = isHighlighted ? (normalColor + Layout.HighlightTint) : normalColor;
        }

        public static Graphic GetButtonGraphic(RectTransform buttonRect)
        {   // Extract graphic component from button hierarchy checking BackOverTint first
            var backOverTint = buttonRect.Find("BackOverTint");
            return backOverTint?.GetComponent<Image>() ?? buttonRect.GetComponent<Graphic>() ?? buttonRect.GetComponentInChildren<Graphic>();
        }

        public static void ClearChildren(Transform parent)
        {   // Destroy all child objects to rebuild panel
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        public static int CalculateFieldWidth(int totalWidth, int columns, int spacing = -1, int padding = -1)
        {   // Calculate individual field width for multi-column layouts accounting for all padding and spacing
            int actualSpacing = spacing < 0 ? Metrics.Spacing : spacing;
            int actualPadding = padding < 0 ? Metrics.Padding * 2 : padding;
            int spacingTotal = actualSpacing * Mathf.Max(0, columns - 1);
            int usableWidth = Mathf.Max(0, totalWidth - actualPadding - spacingTotal);
            return Mathf.Max(Metrics.MinFieldWidth, usableWidth / columns);
        }
    }
}
