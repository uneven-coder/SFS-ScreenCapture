# ScreenCapture

ScreenCapture is a tool for taking very customizable screenshots with controls for background, terrain, rockets, and usefull features like time controll.  

## Example
<img width="1120" height="455" alt="image" src="https://github.com/user-attachments/assets/198bd6ca-5bfa-4e76-a951-3e193b3c9953" />


## Features

### Preview  
Shows a live preview of all enabled features.  
![Preview](gifs/preview.gif)

### Background Controls  
- Toggle background visibility  
- Change background color  
- Make background transparent  
![Background](gifs/background.gif)

### Terrain Controls  
Toggle terrain visibility.  
![Terrain](gifs/terrain.gif)

### Interiors Controls  
Toggle fairings and other interior elements.  
![Interiors](gifs/interiors.gif)

### Rocket Controls  
Hide specific rockets or all rockets.  
![Rockets](gifs/rockets.gif)

### Crop Tool  
Crop the view from left, top, bottom, or right.  
![Crop](gifs/crop.gif)

### Zoom  
Zoom in and out.  
![Zoom](gifs/zoom.gif)

### Quality Settings  
Change screenshot resolution up to system limits to avoid memory issues.  

### Time Controls  
- **Step Forward**: briefly unpause time  
- **Step Back**: return to last paused/forward state  
- **Pause/Resume**: toggle between paused (0x) and normal speed (1x)  
![TimeControls](gifs/time.gif)

---

## Bugs
- **Step Back in Time**: Moving back in time causes particles like rocket engines to not appear active.  
  - Cause: The step-back function loads the last save state, which restores rocket positions but does not reinitialize components, it works the same as loading a quicksave.
  - Fixes: unlikley, posible but would need to be custom for every component that works like this and may cause issues.  

- **Quicksave Loading**: Loading a quicksave reduces the preview window size unexpectedly.
  - Fixes: *Planned*, posible cause is the menu is remade when loading a new scene but loading a quicksave dosent do this and so it uses old values even when their isnt a change.

