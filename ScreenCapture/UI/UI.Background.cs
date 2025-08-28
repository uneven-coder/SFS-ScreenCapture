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
            CreateVerticalContainer(window, 8f, null, TextAnchor.UpperCenter, container => {
                // Toggle for transparency
                Builder.CreateToggleWithLabel(container, 200, 46, () => Transparent, () =>
                {   // Toggle transparency and refresh preview background
                    Transparent = !Transparent;
                }, 0, 0, "Transparent BG");
                
                // RGB color inputs
                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "R", ((int)R).ToString(), val =>
                {
                    if (int.TryParse(val, out int r))
                    {
                        R = Mathf.Clamp(r, 0, 255);
                    }
                });

                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "G", ((int)G).ToString(), val =>
                {
                    if (int.TryParse(val, out int g))
                    {
                        G = Mathf.Clamp(g, 0, 255);
                    }
                });

                Builder.CreateInputWithLabel(container, 200, 40, 0, 0, "B", ((int)B).ToString(), val =>
                {
                    if (int.TryParse(val, out int b))
                    {
                        B = Mathf.Clamp(b, 0, 255);
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

        public static Color GetBackgroundColor() =>
            new Color(R / 255f, G / 255f, B / 255f, Transparent ? 0f : 1f);
    }
}
