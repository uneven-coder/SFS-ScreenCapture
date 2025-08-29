using SFS.UI.ModGUI;
using UnityEngine;
using UnityEngine.UI;
using static UITools.UIToolsBuilder;
using System;

namespace ScreenCapture
{
public abstract class UIBase
{   // Base class for UI windows with shared utilities
        protected Window window;
        protected bool isInitialized;

        public bool IsOpen => window != null;

        public abstract void Show();
        public abstract void Hide();
        public abstract void Refresh();        

        // Delegate types for container content creation
        public delegate void ContainerContentDelegate(Container container);
        
        // Container creation methods with delegate support
        protected static Container CreateContainer(Window parent, SFS.UI.ModGUI.Type layoutType, TextAnchor alignment, 
            float spacing, RectOffset padding, bool childForceExpand, ContainerContentDelegate contentCreator = null)
        {
            var container = Builder.CreateContainer(parent, 0, 0);
            container.CreateLayoutGroup(layoutType, alignment, spacing, padding, childForceExpand);
            
            // Execute the delegate to populate the container if provided
            contentCreator?.Invoke(container);
            
            return container;
        }
        
        // Convenience methods for vertical and horizontal containers
        protected static Container CreateVerticalContainer(Window parent, float spacing = 8f, 
            RectOffset padding = null, TextAnchor alignment = TextAnchor.UpperCenter, 
            ContainerContentDelegate contentCreator = null)
        {
            return CreateContainer(parent, SFS.UI.ModGUI.Type.Vertical, alignment, spacing, 
                padding, true, contentCreator);
        }
        
        protected static Container CreateHorizontalContainer(Window parent, float spacing = 8f, 
            RectOffset padding = null, TextAnchor alignment = TextAnchor.MiddleCenter, 
            ContainerContentDelegate contentCreator = null)
        {
            return CreateContainer(parent, SFS.UI.ModGUI.Type.Horizontal, alignment, spacing, 
                padding, true, contentCreator);
        }
        
        // Nested container creation within existing containers
        protected static Container CreateNestedContainer(Container parent, SFS.UI.ModGUI.Type layoutType, 
            TextAnchor alignment, float spacing, RectOffset padding, bool childForceExpand, 
            ContainerContentDelegate contentCreator = null)
        {
            var container = Builder.CreateContainer(parent, 0, 0);
            container.CreateLayoutGroup(layoutType, alignment, spacing, padding, childForceExpand);
            
            contentCreator?.Invoke(container);
            
            return container;
        }
        
        // Convenience methods for nested vertical and horizontal containers
        protected static Container CreateNestedVertical(Container parent, float spacing = 8f, 
            RectOffset padding = null, TextAnchor alignment = TextAnchor.UpperCenter, 
            ContainerContentDelegate contentCreator = null)
        {
            return CreateNestedContainer(parent, SFS.UI.ModGUI.Type.Vertical, alignment, spacing, 
                padding, true, contentCreator);
        }
        
        protected static Container CreateNestedHorizontal(Container parent, float spacing = 8f, 
            RectOffset padding = null, TextAnchor alignment = TextAnchor.MiddleCenter, 
            ContainerContentDelegate contentCreator = null)
        {
            return CreateNestedContainer(parent, SFS.UI.ModGUI.Type.Horizontal, alignment, spacing, 
                padding, true, contentCreator);
        }

        protected static Container CreateStandardContainer(Window parentWindow, float spacing = 8f)
        {
            var container = Builder.CreateContainer(parentWindow, 0, 0);
            container.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, spacing, null, true);
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
                                                draggable: true, savePosition: true, opacity: 1f, 
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