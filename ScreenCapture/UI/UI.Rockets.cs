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

            SFS.World.PlayerController.main.player.OnChange += DelayedRefreshCallback;
            
            window = CreateStandardWindow(
                World.UIHolder.transform, 
                "Rocket Hierarchy",
                480, 600,
                (int)(World.OwnerInstance.closableWindow.Position.x + 750), 
                (int)World.OwnerInstance.closableWindow.Position.y - 360
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
                    // Toggle all rockets on/off efficiently without LINQ
                    var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                    bool anyVisible = false;
                    foreach (var r in rockets)
                    {
                        if (CaptureUtilities.IsRocketVisible(r))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                    CaptureUtilities.SetAllRocketsVisible(!anyVisible);
                    RefreshRocketList();
                }, "Toggle All");
            });            // Create scrollable list container
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

            var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
            var sortedRockets = new Rocket[rockets.Length];
            System.Array.Copy(rockets, sortedRockets, rockets.Length);
            System.Array.Sort(sortedRockets, (a, b) => string.Compare(a.rocketName ?? a.name, b.rocketName ?? b.name, System.StringComparison.Ordinal));

            foreach (var rocket in sortedRockets)
            {
                string rocketName = !string.IsNullOrWhiteSpace(rocket.mapPlayer.Select_DisplayName) ? 
                               rocket.mapPlayer.Select_DisplayName : rocket.name;
                string label = $"{(rocket.isPlayer ? "> " : "  ")}{rocketName}";

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

        private void DelayedRefreshCallback()
        {   // Callback for player change events with delay
            if (window != null)
                World.OwnerInstance.StartCoroutine(DelayedRefresh());
        }

        private System.Collections.IEnumerator DelayedRefresh()
        {   // Wait for rocket data to update before refreshing
            yield return new WaitForSeconds(0.1f);
            RefreshRocketList();
        }

        public override void Hide()
        {   // Close the rockets window if open
            if (window != null)
            {
                UnityEngine.Object.Destroy(window.gameObject);
                window = null;
            }

            SFS.World.PlayerController.main.player.OnChange -= DelayedRefreshCallback;
            
            ownerRef = null;
            listContent = null;
        }
        
        public override void Refresh()
        {   // Refresh the rocket list
            if (window == null)
                return;
                
            RefreshRocketList();
                
            Hide();
            Show();
        }
    }
}
