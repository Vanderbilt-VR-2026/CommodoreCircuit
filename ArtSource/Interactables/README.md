# Commodore Circuit — Interactables Package

Six on-track interactables, ready to drop into the Unity project: 3D models,
one shared toon material, gameplay scripts, HUD icons and a one-click prefab
builder.

| Model | Type | Tris | What it does |
|---|---|---|---|
| Squirrel | Hazard | ~3.1k | Tail-flick tell, hop-runs across the path, tumbles when hit |
| ConstructionPipe | Hazard | ~1.9k | Clang tell, rolls across the track, biggest penalty |
| StrayFrisbee | Hazard | ~0.6k | Arcs across at handlebar height, spins, gold trail |
| LostFreshman | Hazard | ~2.7k | Zigzags slowly, stops to stare at the map; map flies off when hit |
| SpeedCookie | Power-up | ~0.9k | +40% speed for 2 s |
| MealSwipe | Power-up | ~0.5k | One-hit shield for 8 s |

About 9.8k triangles for all six together, which is light for Quest.

![All six interactables](previews/_all_interactables.jpg)

---

## Setup (about 5 minutes)

**Requires** Unity 2022.3+ with **URP** (the standard Quest setup).

1. **Before copying any art into the repo**, commit `.gitattributes` and run
   `git lfs install`. Otherwise the PNG/FBX/blend files land in normal Git history.
2. Copy `Assets/` into the project's `Assets/` folder (merge, don't replace), and
   `ArtSource/` next to it at the repo root (outside `Assets/`, so Unity ignores it).
3. Let Unity import. The postprocessor applies the correct import settings
   automatically (1 unit = 1 m, no embedded materials, point-filtered palette,
   icons as sprites).
4. Run **Tools → Commodore Circuit → Build Interactable Prefabs**. Six prefabs
   appear in `Assets/Prefabs/Interactables/`.
5. On the player's scooter root (the object with the scooter's collider), add
   **`RacerEffects`**. In the scooter controller, multiply target speed by
   `GetComponent<RacerEffects>().SpeedMultiplier` every frame.
6. For the power-up glow: enable **HDR** on the URP asset and add a **Bloom**
   override to a global Volume (threshold ~1.0).

Commit the generated `.meta` files, the material and the prefabs.

---

## Placing them in the scene

- **Hazards** have waypoint children (`Waypoint_0`, `Waypoint_1`). Drag the
  prefab in, then move the waypoints to lay out where it travels; the yellow
  gizmo line shows the path and the red sphere shows the trigger radius.
- **Power-ups** have no path. Place the root about 0.8–0.9 m off the ground.
- Tuning (penalties, speeds, tell length) is all in the Inspector:
  `Hazard`, `PathMover`, `PowerUp`, `FloatAndSpin`.

Default tuning (placeholders for playtesting):

| | Tell | Speed | Penalty |
|---|---|---|---|
| Squirrel | 0.6 s | 6 m/s | 1 s at 40% speed |
| Pipe | 1.0 s | 3.5 m/s | 2 s at 25% speed + knockback |
| Frisbee | 0.8 s | 7 m/s | 0.5 s at 60% speed |
| Freshman | always-on "?" | 0.7 m/s | 1 s at 40% speed |

Hooks for teammates: `RacerEffects` exposes `onHit`, `onBoost`,
`onShieldStart/Blocked/End` events (wire the vignette, sounds and UI there).
`Hazard`, `PathMover` and `PowerUp` have UnityEvents and AudioSource slots for
the tell/hit/pickup sounds. VR comfort: hits use controller haptics, never
camera shake.

---

## Changing the art

Everything is generated from scripts, so changes are repeatable.

| To change… | Do this |
|---|---|
| A colour on every model | Edit `palette.py`, re-run the build (or repaint the swatch in `CC_Palette.png`) |
| A model's shape | Edit `Interactables.blend` by hand and re-export FBX, **or** edit `build_interactables.py` and re-run |
| HUD icons | Edit `make_hud_icons.py` (or the SVGs) and re-run |

```bash
pip install bpy cairosvg pillow   # bpy = Blender as a Python module
cd ArtSource/Interactables
python -c "from palette import write_textures; write_textures('out/CC_Palette.png','out/CC_Palette_Emission.png')"
python build_interactables.py out out        # FBX + GLB + .blend, prints tri counts
python render_previews.py out/Interactables.blend previews
python make_hud_icons.py icons 512
```

Then copy the new FBX files over the old ones in `Assets/Art/Interactables/Models/`
(same filenames). Prefabs update automatically because the FBX is nested in them.

**Model conventions** (keep these if you hand-edit in Blender):
- Model faces **−Y** in Blender (becomes +Z / forward in Unity). 1 unit = 1 m.
- All UVs sit on a swatch of the shared palette; don't add new materials.
- Parts that animate are separate objects with their pivot at the joint:
  `Tail_1..3`, `Head`, `Body` (squirrel); `Leg_L/R`, `Arm_L/R`, `Torso`, `Head`,
  `Map`, `TellMark` (freshman); `Roller` (pipe); `Disc` (frisbee); `Sparkles`.
  The scripts look these up by name, so keep the names.

---

## Known limits

- **Not yet run in Unity.** The scripts and shader were written for Unity
  2022.3 / URP 14+ but haven't been compiled in a project. Expect possibly a
  small fix or two on first import; the error message will point at the line.
- If the project uses **assembly definitions** in `Assets/Scripts`, move
  `Assets/Scripts/Interactables/Editor/` under an Editor-only asmdef.
- Outlines are inverted-hull, so flat-shaded parts (card, map, star decals)
  can show small gaps at hard corners. Lower `Outline Width` on the material if
  it bothers anyone.
- Bloom costs frame time on Quest. If it drops frames, turn Bloom off; the
  blue swatch still reads clearly as the "grab it" colour.
- The pipe and frisbee teleport back to their start after each run
  (`ResetToStart`). Hide the start behind a fence or tree.
