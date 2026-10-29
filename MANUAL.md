# WaveDude Next manual

<!-- First draft of 2026-10-29, taken from Assets/Legend.md: the figures with their numbered badges, each feature in bold with its tooltip as the description. -->

## The main window

![main-window](Assets/main-window.svg)

The main window with the example project at frame 1200.

1. **Menu bar**
2. **Project pane: the tree of the project's objects**
3. **Properties pane: the selected object's form**
4. **Field picture**
5. **Intensity picture**
6. **Header strip of a picture: title, brightness, Auto**  
   Gain applied to the values before the colour lookup.
7. **Timeline**  
   Click or drag to scrub; drag a bracket to move the start or end of the export range. Left/Right step a frame, PageUp/PageDown ten, Home/End jump to the start/end of the range
8. **Transport controls, position readout, speed, layout**  
   Frame shown / last frame
9. **Status bar**

## The project tree

![project-tree](Assets/project-tree.svg)

The Project pane with the example project.

1. **Pane title**
2. **Add a source**  
   Add a source (appended at the end)
3. **Add a probe**  
   Add a probe (appended at the end)
4. **Add a part (a menu of the kinds)**  
   Add an optical part (a menu of kinds; appended at the end)
5. **Move the selected object up**  
   Move up (Alt+Up); drag to reorder
6. **Move the selected object down**  
   Move down (Alt+Down); drag to reorder
7. **Remove the selected object**  
   Remove (Del)
8. **Row Project**
9. **Row Scene**
10. **Row Simulation**
11. **Row Display**
12. **Row Sources**
13. **Add to this group**
14. **Row Vertical source**
15. **Enabled**  
   Include the source in runs (Space on the row toggles it)
16. **Move up**  
   Move up (Alt+Up); drag to reorder
17. **Move down**  
   Move down (Alt+Down); drag to reorder
18. **Remove**  
   Remove (Del)
19. **Row Horizontal source**
20. **Row Parts**
21. **Row Mirror background**
22. **Row left box**
23. **Row Right box**
24. **Row Connection**
25. **Row Probes**
26. **Row Log Intensity distribution over boxes**
27. **Row Field amplitude diagonal left box**
28. **Row Colour schemes**

## The preview

![preview](Assets/preview.svg)

The preview: field and intensity pictures of the example project at frame 1200.

1. **Field picture**
2. **Brightness of the field picture**  
   Gain applied to the values before the colour lookup.
3. **Auto brightness (fit to the frame shown)**  
   Fit the brightness once to the frame shown: the brightest half percent of it reaches the top of the colour scheme. The brightness then stays as it is.
4. **Intensity picture**
5. **Brightness of the intensity picture**  
   Gain applied to the values before the colour lookup.
6. **Auto brightness of the intensity picture**  
   Fit the brightness once to the frame shown: the brightest half percent of it reaches the top of the colour scheme. The brightness then stays as it is.
7. **Splitter between the pictures**  
   Drag or use the arrow keys to resize; double-click or Enter resets to half
8. **Source handle (Vertical source)**  
   Vertical source: drag to move; with keyboard focus the arrow keys move it by a pixel, ten with Shift
9. **Part handle (Mirror background)**  
   Mirror background: drag to move; with keyboard focus the arrow keys move it by a pixel, ten with Shift
10. **Probe handle (Field amplitude diagonal left box, end A)**  
   Field amplitude diagonal left box: drag to move; with keyboard focus the arrow keys move it by a pixel, ten with Shift
11. **Probe graph box (Field amplitude diagonal left box)**  
   Graph of Field amplitude diagonal left box: drag to move the box; with keyboard focus the arrow keys move it by a pixel, ten with Shift

## The timeline

![timeline](Assets/timeline.svg)

The timeline with the playhead and the in and out points, and the transport controls.

1. **Playhead: the position shown**
2. **In point**
3. **Out point**
4. **Run the simulation**  
   Run the simulation from the start (F5)
5. **Play**  
   Play (Space)
6. **Pause**  
   Pause (Space)
7. **One frame back**  
   Previous frame (,)
8. **One frame forward**  
   Next frame (.)
9. **Set the in point**  
   Start the export range at this frame (I)
10. **Set the out point**  
   End the export range at this frame (O)
11. **Clear the in and out points**  
   Clear the export range: the whole run is exported (X)
12. **Loop**  
   Loop playback over the export range (L)
13. **Live: follow a running run**  
   Follow the newest frame while the run is in progress (F)
14. **Position: frame of frames**  
   Frame shown / last frame
15. **Step of the position**  
   Simulation step of the frame shown
16. **Playback speed**  
   Playback speed: pick a factor or type one
17. **Show the field**  
   Show the field picture
18. **Show the intensity**  
   Show the intensity picture
19. **Pictures side by side**  
   Field and intensity side by side
20. **Pictures stacked**  
   Field above intensity

## The File menu

![menu-file](Assets/menu-file.svg)

The File menu.

1. **New Project**
2. **Open Project...**
3. **Recent Projects**
4. **Examples**
5. **Save**
6. **Save As...**
7. **Export**
8. **Export last**
9. **Close Project**
10. **Exit**

## The Edit menu

![menu-edit](Assets/menu-edit.svg)

The Edit menu.

1. **Undo**
2. **Redo**
3. **Cut**
4. **Copy**
5. **Paste**
6. **Delete**

## The View menu

![menu-view](Assets/menu-view.svg)

The View menu.

1. **Panes**
2. **Theme**
3. **Language**
4. **Preview resolution**
5. **Preview frames**
6. **Preview samples**
7. **Preview dithering**

## The Simulation menu

![menu-simulation](Assets/menu-simulation.svg)

The Simulation menu.

1. **Run**
2. **Pause**
3. **Stop**
4. **Use the GPU (OpenCL)**
5. **Re-run automatically after edits**
6. **RAM cache size**

## The Help menu

![menu-help](Assets/menu-help.svg)

The Help menu.

1. **About WaveDude Next**

## File > Export

![menu-file-export](Assets/menu-file-export.svg)

The File > Export submenu.

1. **Export field...**
2. **Export intensity...**
3. **Export both...**
4. **Export field frame...**
5. **Export intensity frame...**

## File > Examples

![menu-file-examples](Assets/menu-file-examples.svg)

The File > Examples submenu: the shipped example projects.

1. **1. Simple source**
2. **2. Coherent source**
3. **3. Directional pulsed source**
4. **4. 3 frequencies**
5. **5. Small incoherent source**
6. **6. Large incoherent source**
7. **7. Random emitter lattice**
8. **8. Resonant wave in cavity**
9. **9. Laplacian dispersion**
10. **10. Beam expansion**
11. **11. Zone plate**
12. **12. Pulse splitter**
13. **13. Bragg reflector**
14. **14. Resonant roundabout**
15. **15. Top up the tank**
16. **16. Wave in 2 boxes**
17. **17. Hanbury Brown Twiss**
18. **18. XOR AND OR logic**
19. **19. Sources with spherical mirror**
20. **20. Test glass**
21. **21. Ball lens**
22. **22. Coherence and ball lens**
23. **23. Fibers**

## Project

![properties-project](Assets/properties-project.svg)

The project's own properties.

1. **Format version**  
   Version of the project file format.
2. **Random seed**
3. **Description**  
   Free-text notes about the project.
4. **New seed: a fresh random seed**

## Scene

![properties-scene](Assets/properties-scene.svg)

The Scene form (an image scene).

1. **Mode**  
   Blank canvas of the given size, or a canvas built from the scene image.
2. **Scene image**  
   Image the canvas is built from: red = refractive index (red 0 = a mirror), green = sources, blue = damping. The name of the embedded image, or a path relative to the project folder.
3. **Image file**  
   Where the embedded image came from; the embedded image is what is simulated, a missing file changes nothing. The file is watched while the project is open and re-embedded when it changes; on opening the project a changed file replaces the embedded image too.
4. **Embedded image**  
   Size of the embedded scene image; the canvas takes this size
5. **Width**  
   Width of a blank canvas in pixels.
6. **Height**  
   Height of a blank canvas in pixels.
7. **Edges**  
   Open lets a wave leave the grid, as if the canvas sat in open space; Reflective is a hard wall, as in Wave Dude 2D.
8. **Damping edge**  
   Thickness in pixels of the absorbing frame: positive inside the canvas, where it shows as a dark margin; negative outside it, simulated but never shown. 0 turns it off.
9. **Refractive index**  
   Refractive index of a blank canvas, 1.0 to 2.55. The wave speed is 1/n cells per step.
10. **Image sources**  
   Let the green channel of the scene image emit waves; the pixel value is the frequency level.
11. **Image source amplitude**  
   Peak amplitude of the image sources.
12. **Object sources**  
   Use the sources defined in the project.
13. **The image buttons: the dots pick an image file, the save glyph writes the embedded image out, the X drops it**  
   Pick an image file; it is embedded in the project

## Simulation

![properties-simulation](Assets/properties-simulation.svg)

The Simulation form.

1. **Steps**  
   Total number of simulation steps.
2. **Visualize every**  
   Render a frame, export it and sample the probes every this many steps.
3. **4th-order Laplacian**  
   Use the 4th-order stencil with sub-stepping instead of the 9-point stencil. More accurate, slower.
4. **Wave speed**  
   Nominal wave speed in pixels per step; only used to estimate wavelengths in the editor.
5. **Intensity time decay**  
   Averaging window factor of the intensity: the engine averages over this × 100 / lowest source level steps.
6. **Source frequency scale**  
   Multiplier of the level-to-frequency mapping: ω = level / 255 × 0.5 × scale, in radians per step.
7. **Intensity scale**  
   Multiplier applied to the intensity values the engine produces.

## Display

![properties-display](Assets/properties-display.svg)

The Display form.

1. **Colour scheme**  
   Name of the colour scheme mapped onto the values.
2. **Brightness**  
   Gain applied to the values before the colour lookup.
3. **Black level**  
   Offset applied before the colour lookup; values at or below it map to the first colour.
4. **Preview**  
   Show this output in the live preview.
5. **Outline in the preview**  
   Lay the scene image's glowing-edges outline over this output in the preview.
6. **Outline brightness**  
   Multiplier of the outline: 1 as computed, higher lifts faint edges, 0 hides it.
7. **Outline in the export**  
   Bake the outline into exported frames and videos of this output.
8. **Colour scheme**  
   Name of the colour scheme mapped onto the values.
9. **Brightness**  
   Gain applied to the values before the colour lookup.
10. **Black level**  
   Offset applied before the colour lookup; values at or below it map to the first colour.
11. **Preview**  
   Show this output in the live preview.
12. **Outline in the preview**  
   Lay the scene image's glowing-edges outline over this output in the preview.
13. **Outline brightness**  
   Multiplier of the outline: 1 as computed, higher lifts faint edges, 0 hides it.
14. **Outline in the export**  
   Bake the outline into exported frames and videos of this output.
15. **Handles in the preview**  
   Show the handles, lines, emitter dots and levers of every source, part and probe over the preview pictures. A selected object shows its own either way.
16. **Labels in the preview**  
   Show the names beside the handles over the preview pictures. A selected object shows its own either way.
17. **From step**  
   First simulation step of the exported range; 0 is the start of the run.
18. **To step**  
   Last simulation step of the exported range; 0 means the end of the run.
19. **Probe graphs in the export**  
   Draw the probes' graph boxes over exported frames and videos.
20. **Probe markers in the export**  
   Draw the probes' positions (crosses, the line of a line probe, the number) over exported frames and videos.
21. **Source markers in the export**  
   Draw the enabled sources (the marker, a composite source's emitter dots, the name) over exported frames and videos.
22. **Frame rate**  
   Frames per second of the exported video.
23. **Quality**  
   Encoder quality, 1 to 100.

## Source

![properties-source](Assets/properties-source.svg)

A point source.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Include the source in simulation runs.
3. **X**  
   X of the emitter, or of the array centre, in canvas pixels.
4. **Y**  
   Y of the emitter, or of the array centre, in canvas pixels.
5. **Frequency**  
   Frequency as a level up to 255, decimals allowed, the same scale as the green channel of image sources. Above 255 the grid cannot sample the wave.
6. **Amplitude**  
   Amplitude in percent, 0 to 100, decimals allowed.
7. **Phase**  
   Phase offset in degrees, -180 to 180.
8. **Wavelength**  
   Estimated from the wave speed and the frequency (λ = c / f): emitters one wavelength apart radiate in phase along that direction. Holds for the 4th-order Laplacian; the 9-point stencil's speed varies with the frequency
9. **Mode**  
   Emit continuously or in bursts.
10. **Pulse shape**  
   Envelope of each burst; pulsed mode only.
11. **Pulse width**  
   Length of each burst in cycles of the source frequency; pulsed mode only.
12. **Off width**  
   Length of the gap between bursts in cycles; pulsed mode only.
13. **Detach after step**  
   Simulation step at which the source fades out; -1 keeps it on for the whole run.
14. **Composite source**  
   Emit from an array of emitters instead of a single point.
15. **Columns**  
   Number of emitters across. Odd counts keep the centre emitter on the source position.
16. **Rows**  
   Number of emitters down.
17. **Horizontal spacing**  
   Distance between neighbouring columns in canvas pixels.
18. **Vertical spacing**  
   Distance between neighbouring rows in canvas pixels.
19. **Rotation**  
   Rotation of the grid about the source position in degrees, counter-clockwise on the screen. 0 keeps the rows horizontal.
20. **Frequency**  
   Each emitter's level is offset by up to ± this amount. 0 turns it off.
21. **Phase**  
   Each emitter's phase is offset by up to ± this many degrees. 0 turns it off.
22. **Off width**  
   Each gap between bursts is offset by up to ± this many cycles, re-drawn every period. 0 turns it off.
23. **Position**  
   Each emitter is moved by up to ± this many pixels in X and in Y. 0 turns it off.

## Composite source

![properties-composite-source](Assets/properties-composite-source.svg)

A composite (array) source, pulsed.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Include the source in simulation runs.
3. **X**  
   X of the emitter, or of the array centre, in canvas pixels.
4. **Y**  
   Y of the emitter, or of the array centre, in canvas pixels.
5. **Frequency**  
   Frequency as a level up to 255, decimals allowed, the same scale as the green channel of image sources. Above 255 the grid cannot sample the wave.
6. **Amplitude**  
   Amplitude in percent, 0 to 100, decimals allowed.
7. **Phase**  
   Phase offset in degrees, -180 to 180.
8. **Wavelength**  
   Estimated from the wave speed and the frequency (λ = c / f): emitters one wavelength apart radiate in phase along that direction. Holds for the 4th-order Laplacian; the 9-point stencil's speed varies with the frequency
9. **Mode**  
   Emit continuously or in bursts.
10. **Pulse shape**  
   Envelope of each burst; pulsed mode only.
11. **Pulse width**  
   Length of each burst in cycles of the source frequency; pulsed mode only.
12. **Off width**  
   Length of the gap between bursts in cycles; pulsed mode only.
13. **Detach after step**  
   Simulation step at which the source fades out; -1 keeps it on for the whole run.
14. **Composite source**  
   Emit from an array of emitters instead of a single point.
15. **Columns**  
   Number of emitters across. Odd counts keep the centre emitter on the source position.
16. **Rows**  
   Number of emitters down.
17. **Horizontal spacing**  
   Distance between neighbouring columns in canvas pixels.
18. **Vertical spacing**  
   Distance between neighbouring rows in canvas pixels.
19. **Rotation**  
   Rotation of the grid about the source position in degrees, counter-clockwise on the screen. 0 keeps the rows horizontal.
20. **Frequency**  
   Each emitter's level is offset by up to ± this amount. 0 turns it off.
21. **Phase**  
   Each emitter's phase is offset by up to ± this many degrees. 0 turns it off.
22. **Off width**  
   Each gap between bursts is offset by up to ± this many cycles, re-drawn every period. 0 turns it off.
23. **Position**  
   Each emitter is moved by up to ± this many pixels in X and in Y. 0 turns it off.

## Lens

![properties-lens](Assets/properties-lens.svg)

A lens or mirror.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of a lens. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **Diameter**  
   Extent across the axis in pixels.
16. **Concave (diverging)**  
   Whether the lens diverges, its surfaces curving inward. Ticking it turns the lens around by flipping the sign of both radii; a flat surface stays flat.
17. **R1**  
   Radius of the first surface in pixels: positive bulges outward, negative is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.
18. **R2**  
   Radius of the second surface in pixels: negative bulges outward, positive is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.
19. **Centre thickness**  
   Distance between the two surfaces on the axis in pixels.
20. **Edge thickness**  
   Thickness at the rim: the centre thickness less what the convex surfaces take, plus what the concave ones add; 0 when the surfaces meet at the rim

## Polygon

![properties-polygon](Assets/properties-polygon.svg)

A polygon.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **Number of sides**  
   Number of sides, 3 to 64.
16. **Side length**  
   Length of each side in pixels.

## Rectangle

![properties-rectangle](Assets/properties-rectangle.svg)

A rectangle.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **Length**  
   Extent along the axis in pixels.
16. **Width**  
   Extent across the axis in pixels.

## Ellipse

![properties-ellipse](Assets/properties-ellipse.svg)

An ellipse.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **Length**  
   Diameter along the axis in pixels.
16. **Width**  
   Diameter across the axis in pixels.

## Triangle

![properties-triangle](Assets/properties-triangle.svg)

A triangle.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **Type**  
   Equilateral, right isosceles (the 45° prism), 30-60-90, isosceles with one typed angle, or custom with both typed.
16. **Base**  
   Length of the base in pixels; the base lies across the axis, toward a source on the left.
17. **Angle A**  
   Base angle at the first end of the base, in degrees. Typed for the isosceles and custom types; the presets fix it.
18. **Angle B**  
   Base angle at the second end of the base, in degrees. Typed for the custom type; the others fix it.
19. **Apex angle**  
   The angle at the apex: 180° minus the two base angles

## Outline

![properties-outline](Assets/properties-outline.svg)

An outline (free points).

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Mirror**  
   Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.
7. **Refractive index**  
   Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Absorption**  
   How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.
14. **Absorption ramp**  
   Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.
15. **X**
16. **A point: X, Y, remove**  
   Remove this point
17. **Add a point**  
   Append a point after the last one

## Fibre

![properties-fibre](Assets/properties-fibre.svg)

A fibre on a BÃ©zier path.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Core index**  
   Refractive index of the core, 1.00 to 2.55 in steps of 0.01.
7. **Core diameter**  
   Diameter of the core in pixels.
8. **Cladding index**  
   Refractive index of the cladding, 1.00 to 2.55 in steps of 0.01. Below the core's index, the fibre guides.
9. **Cladding thickness**  
   Thickness of the cladding around the core in pixels; 0 for a bare core.
10. **Cladding absorption**  
   Peak absorption of the cladding's absorbing band, 0 (not at all) to 255, as the scene image's blue channel: a hump in the outer part of the cladding, rising over the band width from nothing, peaking, and falling to nothing at the outer edge, so the guided wave's tail stays untouched and stray light is absorbed from either side. Nothing on a bare core.
11. **Absorption band**  
   Half the absorbing band's width in pixels: the absorption rises over this many pixels to its peak and falls over as many to the outer edge. A band wider than the cladding is squeezed into it.
12. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outer boundary and the end faces; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
13. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
14. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
15. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
16. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
17. **Ends**  
   How the two ends are cut: flat, straight across the path like a cleaved fibre, or rounded with the outer radius. The AR coating follows a flat end face.
18. **Node**  
   X relative to the part's centre in canvas pixels, before rotation.
19. **In**  
   X of the in-handle relative to the node, in canvas pixels before rotation. It shapes the segment arriving from the previous node; zero makes that segment straight at this end.
20. **Out**  
   X of the out-handle relative to the node, in canvas pixels before rotation. It shapes the segment leaving to the next node; zero makes that segment straight at this end.
21. **Node**  
   X relative to the part's centre in canvas pixels, before rotation.
22. **In**  
   X of the in-handle relative to the node, in canvas pixels before rotation. It shapes the segment arriving from the previous node; zero makes that segment straight at this end.
23. **Out**  
   X of the out-handle relative to the node, in canvas pixels before rotation. It shapes the segment leaving to the next node; zero makes that segment straight at this end.
24. **Per node: smooth, remove**  
   Remove this node
25. **Add a node**  
   Append a node beyond the last one, continuing the path

## Bragg reflector

![properties-bragg-reflector](Assets/properties-bragg-reflector.svg)

A Bragg reflector.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Layer pairs**  
   Number of layer pairs, 1 to 200.
7. **Thickness 1**  
   Thickness of the first layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.
8. **Index 1**  
   Refractive index of the first layer of each pair, 1.00 to 2.55 in steps of 0.01.
9. **Thickness 2**  
   Thickness of the second layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.
10. **Index 2**  
   Refractive index of the second layer of each pair, 1.00 to 2.55 in steps of 0.01.
11. **Width**  
   Extent of the stack across the axis in pixels.

## GRIN lens

![properties-grin-lens](Assets/properties-grin-lens.svg)

A GRIN lens.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Centre index**  
   Refractive index on the axis, 1.00 to 2.55 in steps of 0.01.
7. **Edge index**  
   Refractive index at the edge, 1.00 to 2.55 in steps of 0.01. The profile between the axis and the edge is parabolic.
8. **AR coating**  
   Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.
9. **Coating profile**  
   How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.
10. **Coat the front**  
   Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.
11. **Coat the back**  
   Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.
12. **Coat the sides**  
   Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.
13. **Length**  
   Extent along the axis in pixels.
14. **Diameter**  
   Extent across the axis in pixels.

## XOR gate

![properties-xor-gate](Assets/properties-xor-gate.svg)

An XOR gate.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Design frequency**  
   The source frequency the gate is designed for, on the same scale as a source's frequency: the frequency of the sources that drive it. The wavelength, and from it the core width, the coupler and the phase section, follow from it, so the gate works at this frequency only.
7. **Wavelength**  
   The wavelength the design frequency gives in a medium of index 1, with the simulation's source frequency scale
8. **Core index**  
   Refractive index of the waveguide cores, 1.00 to 2.55 in steps of 0.01. It must be above the cladding's; the larger the difference, the narrower the cores and the tighter the bends.
9. **Cladding index**  
   Refractive index of the cladding around the cores, 1.00 to 2.55 in steps of 0.01.
10. **AR coating**  
   Thickness in pixels of the anti-reflection band at the ends ticked below, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band, the least reflecting, is λ / (4 √n) pixels thick; the form shows it for the core.
11. **AR on input side**  
   Put the anti-reflection band on the input end. Switch it off where the gate takes its light from a gate before it, so the wave passes through the junction.
12. **AR on output side**  
   Put the anti-reflection band on the output end. Switch it off where the gate hands its light to a gate after it, so the wave passes through the junction.
13. **Quarter-wave coating**  
   The thickness of the least reflecting coating on the core, λ / (4 √n)
14. **Port spacing**  
   Distance between the two inputs, and between the two outputs, in pixels. A value below what the design needs to keep the arms apart is raised to it, and the box shows the spacing used.
15. **Upper output**  
   Let the upper output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
16. **Lower output**  
   Let the lower output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
17. **Core width**
18. **Coupler gap**
19. **Coupler length**
20. **Total length**
21. **Input A**
22. **Input B**
23. **Upper output**
24. **Lower output**

## AND gate

![properties-and-gate](Assets/properties-and-gate.svg)

An AND gate.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Design frequency**  
   The source frequency the gate is designed for, on the same scale as a source's frequency: the frequency of the sources that drive it. The wavelength, and from it the core width, the coupler and the phase section, follow from it, so the gate works at this frequency only.
7. **Wavelength**  
   The wavelength the design frequency gives in a medium of index 1, with the simulation's source frequency scale
8. **Core index**  
   Refractive index of the waveguide cores, 1.00 to 2.55 in steps of 0.01. It must be above the cladding's; the larger the difference, the narrower the cores and the tighter the bends.
9. **Cladding index**  
   Refractive index of the cladding around the cores, 1.00 to 2.55 in steps of 0.01.
10. **AR coating**  
   Thickness in pixels of the anti-reflection band at the ends ticked below, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band, the least reflecting, is λ / (4 √n) pixels thick; the form shows it for the core.
11. **AR on input side**  
   Put the anti-reflection band on the input end. Switch it off where the gate takes its light from a gate before it, so the wave passes through the junction.
12. **AR on output side**  
   Put the anti-reflection band on the output end. Switch it off where the gate hands its light to a gate after it, so the wave passes through the junction.
13. **Quarter-wave coating**  
   The thickness of the least reflecting coating on the core, λ / (4 √n)
14. **Port spacing**  
   Distance between the two inputs, and between the two outputs, in pixels. A value below what the design needs to keep the arms apart is raised to it, and the box shows the spacing used.
15. **Upper output**  
   Let the upper output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
16. **Lower output**  
   Let the lower output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
17. **Core width**
18. **Coupler gap**
19. **Coupler length**
20. **Total length**
21. **Input A**
22. **Input B**
23. **Upper output**
24. **Lower output**

## OR gate

![properties-or-gate](Assets/properties-or-gate.svg)

An OR gate.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Design frequency**  
   The source frequency the gate is designed for, on the same scale as a source's frequency: the frequency of the sources that drive it. The wavelength, and from it the core width, the coupler and the phase section, follow from it, so the gate works at this frequency only.
7. **Wavelength**  
   The wavelength the design frequency gives in a medium of index 1, with the simulation's source frequency scale
8. **Core index**  
   Refractive index of the waveguide cores, 1.00 to 2.55 in steps of 0.01. It must be above the cladding's; the larger the difference, the narrower the cores and the tighter the bends.
9. **Cladding index**  
   Refractive index of the cladding around the cores, 1.00 to 2.55 in steps of 0.01.
10. **AR coating**  
   Thickness in pixels of the anti-reflection band at the ends ticked below, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band, the least reflecting, is λ / (4 √n) pixels thick; the form shows it for the core.
11. **AR on input side**  
   Put the anti-reflection band on the input end. Switch it off where the gate takes its light from a gate before it, so the wave passes through the junction.
12. **AR on output side**  
   Put the anti-reflection band on the output end. Switch it off where the gate hands its light to a gate after it, so the wave passes through the junction.
13. **Quarter-wave coating**  
   The thickness of the least reflecting coating on the core, λ / (4 √n)
14. **Port spacing**  
   Distance between the two inputs, and between the two outputs, in pixels. A value below what the design needs to keep the arms apart is raised to it, and the box shows the spacing used.
15. **Upper output**  
   Let the upper output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
16. **Lower output**  
   Let the lower output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.
17. **Core width**
18. **Coupler gap**
19. **Coupler length**
20. **Total length**
21. **Input A**
22. **Input B**
23. **Upper output**
24. **Lower output**

## Zone plate

![properties-zone-plate](Assets/properties-zone-plate.svg)

A zone plate.

1. **Name**  
   Name shown in the project tree and on the canvas.
2. **Enabled**  
   Paint the part into the scene; a disabled part has no effect on a run.
3. **X**  
   X of the part's centre in canvas pixels.
4. **Y**  
   Y of the part's centre in canvas pixels.
5. **Angle**  
   Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.
6. **Target frequency**  
   The source frequency the plate is designed for, on the same scale as a source's frequency. The zones follow from the wavelength it gives, so the plate focuses this frequency at the focal length; other frequencies focus elsewhere.
7. **Wavelength**  
   The wavelength the design frequency gives in a medium of index 1, with the simulation's source frequency scale
8. **Focal length**  
   Distance in pixels from the plate to its focus, for a plane wave arriving along the axis.
9. **Inverse**  
   Exchange the zones: the central zone becomes a block, the first ring open, and so on. The focus stays where it is.
10. **Diameter**  
   Extent across the axis in pixels; the zones fill it.
11. **Thickness**  
   Extent along the axis in pixels: the depth of the blocks. It should leave room for the anti-reflection rise on both faces.
12. **Anti-reflection**  
   Depth in pixels over which a block's absorption rises from nothing at its faces to its full strength, so the blocks reflect little; at most half the thickness takes effect.
13. **Mount**  
   Surround the plate with an absorbing mount, so no light passes outside its diameter. Without it, that light meets the focused light on the axis and moves the focus further out, the more so the fewer zones the plate has.
14. **Mount width**  
   Width in pixels of the absorbing mount on either side, beyond the diameter.
15. **Number of zones**
16. **Outermost zone**

## Probe

![properties-probe](Assets/properties-probe.svg)

A point probe of the field.

1. **Name**  
   Name shown in the project tree and in the graph title.
2. **Active**  
   Sample and draw this probe.
3. **Start after step**  
   Step at which recording starts; 0 records from the first step.
4. **Layer**  
   Output the probe samples and is drawn on.
5. **Measurement**  
   Recorded quantity. The field offers its amplitude only; the intensity layer its value, or the value integrated over the run.
6. **Kind**  
   A single point, or a line from A to B.
7. **A x**  
   X of the probe pixel, or of the start of the line, in canvas pixels.
8. **A y**  
   Y of the probe pixel, or of the start of the line, in canvas pixels.
9. **B x**  
   X of the end of the line in canvas pixels; line probes only.
10. **B y**  
   Y of the end of the line in canvas pixels; line probes only.
11. **Box size**  
   Nominal size of the graph box drawn over the output; a box larger than the canvas is shrunk to fit it, aspect kept.
12. **Box x**  
   Left edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.
13. **Box y**  
   Top edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.
14. **Box width**  
   Width of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.
15. **Box height**  
   Height of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.
16. **Step axis**  
   Sliding window of the last steps, or the whole run; point probes only.
17. **Window**  
   Width of the sliding step window, in steps.
18. **Auto-scale steps**  
   Let the step axis follow the samples recorded so far; point probes only.
19. **Auto-scale values**  
   Fit the value axis to the data instead of the fixed minimum and maximum.
20. **Value axis**  
   A linear or a logarithmic value axis; intensity probes only, the field is always linear.
21. **Decades**  
   Decades a logarithmic value axis spans below its top; smaller values, zero included, sit on the bottom line.
22. **Value minimum**  
   Lower bound of the value axis when it is not auto-scaled.
23. **Value maximum**  
   Upper bound of the value axis when it is not auto-scaled.

## Line probe

![properties-line-probe](Assets/properties-line-probe.svg)

A line probe of the intensity, logarithmic.

1. **Name**  
   Name shown in the project tree and in the graph title.
2. **Active**  
   Sample and draw this probe.
3. **Start after step**  
   Step at which recording starts; 0 records from the first step.
4. **Layer**  
   Output the probe samples and is drawn on.
5. **Measurement**  
   Recorded quantity. The field offers its amplitude only; the intensity layer its value, or the value integrated over the run.
6. **Kind**  
   A single point, or a line from A to B.
7. **A x**  
   X of the probe pixel, or of the start of the line, in canvas pixels.
8. **A y**  
   Y of the probe pixel, or of the start of the line, in canvas pixels.
9. **B x**  
   X of the end of the line in canvas pixels; line probes only.
10. **B y**  
   Y of the end of the line in canvas pixels; line probes only.
11. **Box size**  
   Nominal size of the graph box drawn over the output; a box larger than the canvas is shrunk to fit it, aspect kept.
12. **Box x**  
   Left edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.
13. **Box y**  
   Top edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.
14. **Box width**  
   Width of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.
15. **Box height**  
   Height of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.
16. **Step axis**  
   Sliding window of the last steps, or the whole run; point probes only.
17. **Window**  
   Width of the sliding step window, in steps.
18. **Auto-scale steps**  
   Let the step axis follow the samples recorded so far; point probes only.
19. **Auto-scale values**  
   Fit the value axis to the data instead of the fixed minimum and maximum.
20. **Value axis**  
   A linear or a logarithmic value axis; intensity probes only, the field is always linear.
21. **Decades**  
   Decades a logarithmic value axis spans below its top; smaller values, zero included, sit on the bottom line.
22. **Value minimum**  
   Lower bound of the value axis when it is not auto-scaled.
23. **Value maximum**  
   Upper bound of the value axis when it is not auto-scaled.

## Colour scheme

![properties-colour-scheme](Assets/properties-colour-scheme.svg)

A colour scheme of the project.

1. **Name**  
   Name the display settings refer to.
2. **Kind**  
   Output the scheme is meant for; the pickers list it for that output only.
3. **Description**  
   Free text: what the scheme is, where it comes from.
4. **Based on**  
   Copy the stops of an existing scheme into this one, then change them
5. **A stop: position, colour swatch (opens the picker), hex value, remove**  
   Pick the colour: Space opens the picker, Enter keeps it, Escape puts the old colour back
6. **Add a stop**  
   Insert a stop in the widest gap, with the gradient's colour there

