using System;
using SFS.UI.ModGUI;
using SFS.Utilities;
using UnityEngine;
using static ScreenCapture.Main;

namespace ScreenCapture
{
    internal class BackgroundUI : UIBase
    {   // Manage background color/transparency UI and state
        public static float R = 0f, G = 0f, B = 0f;
        public static bool Transparent = false;

        private Captue ownerRef;

        public override void Show()
        {   // Show the background settings window at default position
            if (World.OwnerInstance.closableWindow == null)
                return;

            if (IsOpen)
                return;

            window = CreateStandardWindow(
                World.UIHolder.transform,
                "Background",
                280, 320,
                (int)(World.OwnerInstance.closableWindow.Position.x + 650),
                (int)World.OwnerInstance.closableWindow.Position.y
            );

            // Create content using the delegate approach for cleaner organization
            CreateVerticalContainer(window, 8f, null, TextAnchor.UpperCenter, container =>
            {
                // Toggle for transparency
                Builder.CreateToggleWithLabel(container, 200, 46, () => Transparent, () =>
                {   // Toggle transparency and refresh preview background
                    Transparent = !Transparent;
                    Debug.Log($"BackgroundUI: Transparent set to {Transparent}");  
                    World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                }, 0, 0, "Transparent BG");

                // RGB color inputs
                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "R", ((int)R).ToString(), val =>
                {
                    if (int.TryParse(val, out int r))
                    {
                        R = Mathf.Clamp(r, 0, 255);
                        Debug.Log($"BackgroundUI: R -> {R}");  
                        World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                    }
                });

                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "G", ((int)G).ToString(), val =>
                {
                    if (int.TryParse(val, out int g))
                    {
                        G = Mathf.Clamp(g, 0, 255);
                        Debug.Log($"BackgroundUI: G -> {G}");  
                        World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                    }
                });

                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "B", ((int)B).ToString(), val =>
                {
                    if (int.TryParse(val, out int b))
                    {
                        B = Mathf.Clamp(b, 0, 255);
                        Debug.Log($"BackgroundUI: B -> {B}");  
                        World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                    }
                });

                // Help label
                Builder.CreateLabel(container, 200, 35, 0, 0, "RGB out of 255");
            });
        }

        public override void Hide()
        {   // Close the background window if open
            if (window != null)
            {
                UnityEngine.Object.Destroy(window.gameObject);
                window = null;
            }

            ownerRef = null;
        }

        public override void Refresh()
        {   // Refresh UI with current values
            if (window == null)
                return;

            Hide();
            Show();
        }

        public static Color GetBackgroundColor()
        {   // Always use user-selected RGBA to support custom color or transparency
            ref bool backgroundEnabled = ref World.OwnerInstance.showBackground;

            float a = Transparent ? 0f : 1f;
            return backgroundEnabled ? new Color(0f, 0f, 0f, 1f) : new Color(R / 255f, G / 255f, B / 255f, a) ;
            
        }
    }
}
