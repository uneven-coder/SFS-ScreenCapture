using SFS.UI.ModGUI;
using UnityEngine;
using UnityEngine.UI;
using static UITools.UIToolsBuilder;

namespace ScreenCapture
{
public abstract class UIBase
{   // Base class for UI windows with shared utilities
        protected Window window;
        protected bool isInitialized;

        public bool IsOpen => window != null;

        // Abstract methods to be implemented by each UI component
        public abstract void Show(Captue owner);
        public abstract void Hide();
        public abstract void Refresh();        protected static Container CreateStandardContainer(Window parentWindow, float spacing = 8f)
        {
            var container = Builder.CreateContainer(parentWindow, 0, 0);
            container.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, spacing, null, true);
            return container;
        }

        protected static Container CreateHorizontalContainer(Window parentWindow, float spacing = 8f)
        {
            var container = Builder.CreateContainer(parentWindow, 0, 0);
            container.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, spacing, null, true);
            return container;
        }

        protected static void SetupScrollableContent(Container container)
        {
            var fitter = container.gameObject.GetComponent<ContentSizeFitter>() ?? 
                         container.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        protected static Window CreateStandardWindow(Transform parent, string title, int width, int height, int x, int y)
        {
            int id = Builder.GetRandomID();
            var newWindow = CreateClosableWindow(parent, id, width, height, x, y, 
                                                draggable: true, savePosition: false, opacity: 1f, 
                                                titleText: title);
            newWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, 
                                       TextAnchor.UpperCenter, 16f, 
                                       new RectOffset(6, 6, 6, 6), true);
            return newWindow;
        }

        public static void DisableRaycastsInChildren(GameObject root)
        {
            if (root == null)
                return;

            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }

        public static void SetTextInputColor(TextInput input, Color color)
        {
            if (input == null)
                return;

            var img = input.gameObject.GetComponent<Image>();
            if (img != null)
                img.color = color;

            var text = input.gameObject.GetComponentInChildren<Text>(true);
            if (text != null)
                text.color = color;
        }
        
        public static string FormatMB(long bytes) =>
            $"{bytes / (1024.0 * 1024.0):0.#} MB";

        protected static void ForceRebuildLayout(RectTransform rectTransform)
        {
            if (rectTransform != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }
    }
}