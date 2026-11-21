GLS Axis Unlock Test

Install and compare

1. Add a song.ogg at least 34 seconds long and copy the map folder into CustomWIPLevels.
2. Leave the standalone GlsAxisUnlock.dll prototype inactive for both runs.
3. Turn off any environment override and enable full environment effects.
4. Play with No Fail. Watch the bright pieces and use the dim pieces as references.
5. Run each difficulty with ChromaGLS off. Return to the menu, enable ChromaGLS, and replay.
6. For a metadata control, remove the ChromaGLS suggestion from the difficulty in Info.dat, reload the map, and replay with the mod enabled. Only the wired axes should respond.

Times are measured from the start of the song.

Easy | The Rolling Stones | Cube pieces on both sides

Time       What to watch
02–06 s    Native Z translation. The bright pieces move in both runs.
07–11 s    Unwired X translation. The bright pieces move sideways with ChromaGLS on.
12–16 s    Unwired Y translation. The bright pieces move up or down with ChromaGLS on.
17–21 s    Native X and Y rotation. The pieces tilt, then turn, in both runs.
22–26 s    Unwired Z rotation. The pieces roll with ChromaGLS on.
27–32 s    The native X tilt holds while the unlocked Z axis rolls the piece.

During the Z roll, the right cubes use their X axis settings for mirroring.
Their X rotation is mirrored, and their Y rotation is unmirrored.
With ChromaGLS off, the unwired axes cause no motion.

Normal | Daft Punk | Runway and triangle walls

Time       What to watch
02–06 s    Native X translation on the runway edge pieces.
07–11 s    Native Y translation on the same pieces.
12–16 s    Unwired Z translation on the runway edges.
17–21 s    The native X offset holds while the unlocked Z axis slides the pieces.
22–26 s    Native Z translation on the triangles.
27–33 s    Unwired X and Y translation on the triangles.

At 13 s, both runway halves receive the same positive Z value.
The left edge uses its mirrored X settings, so its local Z value is negative.
The right edge uses its unmirrored X settings, so its local Z value is positive.
The runway edges stay at rest in the Z-only section with ChromaGLS off.
The triangles use their Z transform and limits for both new axes.

Environment sources

The Rolling Stones uses group 0 (CubePiecesL) and group 1 (CubePiecesR).
Daft Punk uses runway groups 25 and 26 and triangle wall groups 0 and 1.
The group IDs, transform axes, limits, and mirror flags match the exported
Beat Saber 1.44.3 and 1.45.0 scenes.

The map uses standard V3 event boxes.
