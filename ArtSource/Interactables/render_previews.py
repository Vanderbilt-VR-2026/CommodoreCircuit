"""Renders a 3/4 preview PNG of each model in Interactables.blend, with
Freestyle outlines to approximate the in-game toon look.

    python render_previews.py <Interactables.blend> <out_dir>
"""

import math
import os
import sys

import bpy
from mathutils import Vector

VIEWS = {  # model -> (azimuth deg, elevation deg)  — azimuth 0 = looking from -Y (front)
    "Squirrel": (-35, 12),
    "ConstructionPipe": (-30, 18),
    "StrayFrisbee": (-25, 35),
    "LostFreshman": (-30, 10),
    "SpeedCookie": (-25, 10),
    "MealSwipe": (-25, 10),
}


def hierarchy(o):
    out = [o]
    for c in o.children:
        out += hierarchy(c)
    return out


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def main():
    blend, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=blend)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 24
    sc.cycles.use_denoising = False
    sc.render.resolution_x = sc.render.resolution_y = 640
    sc.render.film_transparent = True
    sc.view_settings.view_transform = 'Standard'
    sc.render.use_freestyle = True
    sc.render.line_thickness_mode = 'ABSOLUTE'
    sc.render.line_thickness = 2.2
    ls = sc.view_layers[0].freestyle_settings.linesets[0]
    ls.select_by_visibility = True
    ls.select_silhouette = ls.select_border = True
    ls.select_crease = False
    if ls.linestyle is None:
        ls.linestyle = bpy.data.linestyles.new("Outline")
    ls.linestyle.color = (0.08, 0.08, 0.08)

    world = bpy.data.worlds.new("W")
    sc.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.85, 0.9, 0.95, 1)
    bg.inputs[1].default_value = 0.9

    sun_d = bpy.data.lights.new("Sun", 'SUN')
    sun_d.energy = 3.2
    sun_d.angle = math.radians(8)
    sun = bpy.data.objects.new("Sun", sun_d)
    sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 50
    cam = bpy.data.objects.new("Cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam

    colls = [c for c in sc.collection.children if c.name in VIEWS]
    for c in colls:
        for other in colls:
            other.hide_render = other is not c
        root = next(o for o in c.objects if o.parent is None)
        objs = hierarchy(root)
        lo, hi = bounds(objs)
        center = (lo + hi) / 2
        radius = (hi - lo).length / 2
        az, el = map(math.radians, VIEWS[c.name])
        d = radius / math.tan(math.radians(18)) * 1.05
        direction = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
        cam.location = center + direction * d
        cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out_dir, f"{c.name}.png")
        bpy.ops.render.render(write_still=True)
        print("[rendered]", c.name)


if __name__ == "__main__":
    main()
