using System.Linq;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using static ScreenCapture.Main;

namespace ScreenCapture
{
    internal class RocketsUI : UIBase
    {   // Manage rocket hierarchy UI and visibility toggles
        private Captue ownerRef;
        private Container listContent;

        public override void Show()
        {   // Show the rockets window at default position
            if (World.OwnerInstance.closableWindow == null)
                return;
            
            if (IsOpen)
                return;
            
            window = CreateStandardWindow(
                World.UIHolder.transform, 
                "Rocket Hierarchy",
                480, 600, (int)(World.OwnerInstance.closableWindow.rectTransform.position.x + 10), (int)World.OwnerInstance.closableWindow.rectTransform.position.y
            );
            
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);
            
            // Create header with buttons using delegate-based container
            CreateVerticalContainer(window, 8f, null, TextAnchor.UpperCenter, header => {
                // Add refresh button
                Builder.CreateButton(header, 400, 46, 0, 0, () => {
                    // Refresh the rocket hierarchy list
                    RefreshRocketList();
                }, "Refresh");

                // Add toggle all button
                Builder.CreateButton(header, 400, 46, 0, 0, () => {
                    // Toggle all rockets on/off
                    var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                    bool anyVisible = rockets.Any(r => CaptureUtilities.IsRocketVisible(r)); 
                    CaptureUtilities.SetAllRocketsVisible(!anyVisible);
                    RefreshRocketList();
                }, "Toggle All");
            });

            // Create scrollable list container
            listContent = CreateVerticalContainer(window, 18f);
            // SetupScrollableContent(listContent);

            RefreshRocketList();
        }

        private void RefreshRocketList()
        {   // Rebuild the rocket list and bind visibility toggles
            if (listContent == null)
                return;
                
            foreach (Transform child in listContent.gameObject.transform)
                UnityEngine.Object.Destroy(child.gameObject);

            var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true)
                           .OrderBy(r => r.rocketName ?? r.name)
                           .ToArray();

            foreach (var rocket in rockets)
            {
                string label = !string.IsNullOrWhiteSpace(rocket.mapPlayer.Select_DisplayName) ? 
                               rocket.mapPlayer.Select_DisplayName : rocket.name;

                var toggle = Builder.CreateToggleWithLabel(listContent, 400, 34, 
                    () => CaptureUtilities.IsRocketVisible(rocket), 
                    () =>
                    {   // Toggle per-rocket visibility
                        bool cur = CaptureUtilities.IsRocketVisible(rocket); 
                        CaptureUtilities.SetRocketVisible(rocket, !cur);
                    }, 
                    0, 0, label);

                var toggleLE = toggle.gameObject.GetComponent<LayoutElement>() ?? 
                               toggle.gameObject.AddComponent<LayoutElement>();
                toggleLE.minHeight = 30f; 
                toggleLE.preferredHeight = 34f;
            }

            ForceRebuildLayout(listContent.rectTransform);
        }

        public override void Hide()
        {   // Close the rockets window if open
            if (window != null)
            {
                UnityEngine.Object.Destroy(window.gameObject);
                window = null;
            }
            
            ownerRef = null;
            listContent = null;
        }
        
        public override void Refresh()
        {   // Refresh the rocket list
            if (window == null)
                return;
                
            RefreshRocketList();
        }
    }
}
