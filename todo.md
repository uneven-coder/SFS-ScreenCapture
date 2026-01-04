ui sort args by

module: the name of shader (needed when shaders use multiple)
category: makes it easy to use
finaly the label and input


create helper functions to better organise ui code so its expandable and layout is shown in code


its not applying the args to the material
make it use scaled and world cam

the gradients are the wrong way they neet to rotate -90
clouds are darker closer to sunset even when not close
make clouds have different rotation directions and speeds, use same noise as cloudnoisethresholdscale / cloudthresholdvariation

Fix gradient rotation: The gradient is being sampled based on height (vertical), but needs to be rotated -90 degrees, meaning it should sample based on the horizontal position relative to the sun direction (day/night terminator).

Improve atmospheric scattering model: Implement a more physically-based Rayleigh and Mie scattering model for realistic day/night transitions.

Add proper limb darkening: Atmosphere should be thicker and more colorful at the edges (limb) due to longer light paths.

Improve terminator rendering: Create a smoother, more realistic transition between day and night sides.

Add atmospheric refraction effects: Light bending near the horizon for more realistic sunset/sunrise colors.


add a blackwhole planet
make a sun shader


shader