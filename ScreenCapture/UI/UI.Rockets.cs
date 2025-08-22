using System.Linq;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;

namespace ScreenCapture
{
    internal class RocketsUI : UIBase
    {   // Manage rocket hierarchy UI and visibility toggles
        private Captue ownerRef;
        private Container listContent;

        public override void Show(Captue owner)
        {   // Create the rockets window anchored next to main window
            if (owner == null || owner.closableWindow == null || IsOpen)
                return;

            ownerRef = owner;
            
            window = CreateStandardWindow(
                owner.uiHolder.transform, 
                "Rocket Hierarchy",
                480, 600, 
                (int)(owner.closableWindow.Position.x + owner.closableWindow.Size.x + 10), 
                (int)owner.closableWindow.Position.y
            );
            
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);
            
            var header = CreateStandardContainer(window, 8f);

            listContent = CreateStandardContainer(window, 18f);
            SetupScrollableContent(listContent);

            Builder.CreateButton(header, 400, 46, 0, 0, () =>
            {   // Refresh the rocket hierarchy list
                RefreshRocketList();
            }, "Refresh");

            Builder.CreateButton(header, 400, 46, 0, 0, () =>
            {   // Toggle all rockets on/off
                var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                bool anyVisible = rockets.Any(r => ownerRef.IsRocketVisible(r)); 
                ownerRef.SetAllRocketsVisible(!anyVisible);
                RefreshRocketList();
            }, "Toggle All");

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
                    () => ownerRef.IsRocketVisible(rocket), 
                    () =>
                    {   // Toggle per-rocket visibility
                        bool cur = ownerRef.IsRocketVisible(rocket); 
                        ownerRef.SetRocketVisible(rocket, !cur);
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
