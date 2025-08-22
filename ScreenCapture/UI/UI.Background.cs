using SFS.UI.ModGUI;
using UnityEngine;

namespace ScreenCapture
{
    internal class BackgroundUI : UIBase
    {   // Manage background color/transparency UI and state
        public static float R = 0f, G = 0f, B = 0f;
        public static bool Transparent = false;
        
        private Captue ownerRef;
        
        public override void Show(Captue owner)
        {   // Show the background settings window next to the main window
            if (owner == null || owner.closableWindow == null)
                return;
                
            ownerRef = owner;
            
            if (IsOpen)
                return;
                
            window = CreateStandardWindow(
                owner.uiHolder.transform, 
                "Background",
                280, 320, 
                (int)(owner.closableWindow.Position.x + 700), 
                (int)owner.closableWindow.Position.y
            );
            
            var content = CreateStandardContainer(window, 8f);
            
            Builder.CreateToggleWithLabel(content, 200, 46, () => Transparent, () =>
            {   // Toggle transparency and refresh preview bg
                Transparent = !Transparent;
                if (ownerRef?.previewCamera != null)
                    CaptureUtilities.ApplyBackgroundSettingsToCamera(ownerRef, ownerRef.previewCamera);
            }, 0, 0, "Transparent BG");

            Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "R", ((int)R).ToString(), val =>
            {
                if (int.TryParse(val, out int r))
                {
                    R = Mathf.Clamp(r, 0, 255);
                    if (ownerRef?.previewCamera != null)
                        CaptureUtilities.ApplyBackgroundSettingsToCamera(ownerRef, ownerRef.previewCamera);
                }
            });

            Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "G", ((int)G).ToString(), val =>
            {
                if (int.TryParse(val, out int g))
                {
                    G = Mathf.Clamp(g, 0, 255);
                    if (ownerRef?.previewCamera != null)
                        CaptureUtilities.ApplyBackgroundSettingsToCamera(ownerRef, ownerRef.previewCamera);
                }
            });

            Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "B", ((int)B).ToString(), val =>
            {
                if (int.TryParse(val, out int b))
                {
                    B = Mathf.Clamp(b, 0, 255);
                    if (ownerRef?.previewCamera != null)
                        CaptureUtilities.ApplyBackgroundSettingsToCamera(ownerRef, ownerRef.previewCamera);
                }
            });

            Builder.CreateLabel(content, 200, 35, 0, 0, "RGB out of 255");
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
            Show(ownerRef);
        }

        public static Color GetBackgroundColor() =>
            new Color(R / 255f, G / 255f, B / 255f, Transparent ? 0f : 1f);
    }
}
