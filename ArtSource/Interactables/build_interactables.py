"""Builds every Commodore Circuit interactable procedurally in Blender.

Run headless (Blender 4.2+ / bpy module):
    blender -b -P build_interactables.py -- <out_dir>
    python build_interactables.py <out_dir>          # with the `bpy` pip module

Outputs, per model: <Name>.fbx (Unity), <Name>.glb, plus Interactables.blend
with every model in its own collection for hand-editing.

Conventions (match Unity after FBX import):
  * 1 Blender unit = 1 metre.
  * Models FACE -Y in Blender, which becomes +Z (forward) in Unity.
  * Every part's UVs point at one swatch of the shared palette (palette.py),
    so all models share one material.
  * Parts that animate are separate objects with their pivot at the joint
    (squirrel tail/head, freshman legs/arms/map, pipe roller, sparkles).
"""

import math
import os
import random
import sys

import bpy  # noqa: F401  (must load before bmesh)
import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palette import slot_uv  # noqa: E402

UV_NAME = "UVMap"


# --------------------------------------------------------------------------
# bmesh primitives — each returns a fresh BMesh, already transformed
# --------------------------------------------------------------------------

def _xf(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    return (Matrix.Translation(loc)
            @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
            @ Matrix.Diagonal((*scale, 1.0)))


def _apply(bm, m):
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bm


def sphere(r=1.0, loc=(0, 0, 0), scale=(1, 1, 1), rot=(0, 0, 0), u=14, v=9):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    return _apply(bm, _xf(loc, rot, scale))


def cone(r1, r2, depth, segs=12, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), caps=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs,
                          radius1=r1, radius2=r2, depth=depth)
    return _apply(bm, _xf(loc, rot, scale))


def box(size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    return _apply(bm, _xf(loc, rot, size))


def limb(p1, p2, r1, r2=None, segs=10):
    """Tapered cylinder from p1 to p2 (for arms / legs / straps)."""
    p1, p2 = Vector(p1), Vector(p2)
    d = p2 - p1
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs,
                          radius1=r1, radius2=r2 if r2 is not None else r1, depth=d.length)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    return _apply(bm, Matrix.Translation((p1 + p2) / 2) @ rot)


def lathe(profile, segs=20, axis_rot=(0, 0, 0), loc=(0, 0, 0)):
    """Revolve a (radius, z) profile around Z. Profile runs top -> bottom;
    points with radius 0 become poles."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segs),
                                        r * math.sin(2 * math.pi * i / segs), z))
                          for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _apply(bm, _xf(loc, axis_rot))


def torus(major, minor, maj_segs=24, min_segs=8, loc=(0, 0, 0), rot=(0, 0, 0),
          arc=(0.0, 360.0)):
    """Torus around Z. `arc` limits the sweep in degrees (open ends are left open)."""
    bm = bmesh.new()
    a0, a1 = map(math.radians, arc)
    full = abs((a1 - a0) - 2 * math.pi) < 1e-4
    n = maj_segs if full else maj_segs + 1
    grid = []
    for i in range(n):
        t = a0 + (a1 - a0) * i / maj_segs
        c = Vector((major * math.cos(t), major * math.sin(t), 0))
        out = Vector((math.cos(t), math.sin(t), 0))
        grid.append([bm.verts.new(c + minor * (math.cos(p) * out + Vector((0, 0, math.sin(p)))))
                     for p in (2 * math.pi * k / min_segs for k in range(min_segs))])
    rng = range(maj_segs) if full else range(maj_segs)
    for i in rng:
        i2 = (i + 1) % n
        for k in range(min_segs):
            k2 = (k + 1) % min_segs
            bm.faces.new((grid[i][k], grid[i2][k], grid[i2][k2], grid[i][k2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _apply(bm, _xf(loc, rot))


def prism(poly, depth, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Extrude a 2D polygon (XY, CCW or CW) along Z, centred on z=0."""
    bm = bmesh.new()
    top = [bm.verts.new((x, y, depth / 2)) for x, y in poly]
    bot = [bm.verts.new((x, y, -depth / 2)) for x, y in poly]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    n = len(poly)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _apply(bm, _xf(loc, rot, scale))


def star_poly(points=5, r_out=1.0, r_in=0.45, start=90.0):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(start + i * 180.0 / points)
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


def rounded_rect(w, h, r, segs=4):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90),
                       (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(segs + 1):
            a = math.radians(a0 + 90 * k / segs)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


V_POLY = [(-0.5, 0.5), (-0.22, 0.5), (0.0, -0.08), (0.22, 0.5), (0.5, 0.5),
          (0.13, -0.5), (-0.13, -0.5)]


# --------------------------------------------------------------------------
# Part builder: merges coloured parts into one object
# --------------------------------------------------------------------------

def paint(bm, color, smooth=True):
    """Point every loop of `bm` at a palette swatch. `color` may be a name or
    a function(face) -> name for per-face colouring."""
    uv = bm.loops.layers.uv.get(UV_NAME) or bm.loops.layers.uv.new(UV_NAME)
    for f in bm.faces:
        name = color(f) if callable(color) else color
        u = slot_uv(name)
        for loop in f.loops:
            loop[uv].uv = u
        f.smooth = smooth
    return bm


class Part:
    def __init__(self):
        self.bm = bmesh.new()

    def add(self, bm, color, smooth=True):
        paint(bm, color, smooth)
        tmp = bpy.data.meshes.new("_tmp")
        bm.to_mesh(tmp)
        bm.free()
        self.bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        return self

    def build(self, name, parent=None, pivot=(0, 0, 0), collection=None):
        """Create the object. Geometry is authored relative to `pivot`
        (joint position in the PARENT's space)."""
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces,
                              quad_method='BEAUTY', ngon_method='EAR_CLIP')
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        obj = bpy.data.objects.new(name, mesh)
        (collection or bpy.context.scene.collection).objects.link(obj)
        obj.location = pivot
        if parent:
            obj.parent = parent
        obj.data.materials.append(palette_material())
        return obj


def empty(name, collection, parent=None, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.2
    collection.objects.link(e)
    e.location = loc
    if parent:
        e.parent = parent
    return e


_MAT = None
TEX_DIR = None


def palette_material():
    """Blender-side preview material (Unity uses CC_ToonLit instead)."""
    global _MAT
    if _MAT:
        return _MAT
    m = bpy.data.materials.new("CC_Interactables")
    try:
        m.use_nodes = True
    except AttributeError:
        pass
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        out = next((n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'), None) \
            or nt.nodes.new("ShaderNodeOutputMaterial")
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.85
    base = nt.nodes.new("ShaderNodeTexImage")
    base.image = bpy.data.images.load(os.path.join(TEX_DIR, "CC_Palette.png"))
    base.interpolation = 'Closest'
    emis = nt.nodes.new("ShaderNodeTexImage")
    emis.image = bpy.data.images.load(os.path.join(TEX_DIR, "CC_Palette_Emission.png"))
    emis.image.colorspace_settings.name = 'sRGB'
    emis.interpolation = 'Closest'
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    ec = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
    nt.links.new(emis.outputs["Color"], ec)
    bsdf.inputs["Emission Strength"].default_value = 2.5
    _MAT = m
    return m


def sparkles(parent, coll, radius, count=3, size=0.06, plane="XZ", height=0.0):
    """Child 'Sparkles' with small 4-point stars that FloatAndSpin orbits."""
    root = empty("Sparkles", coll, parent)
    p = Part()
    for i in range(count):
        a = 2 * math.pi * i / count + 0.4
        if plane == "XZ":
            loc = (radius * math.cos(a), 0.0, height + radius * math.sin(a))
            rot = (90, 0, 0)
        else:
            loc = (radius * math.cos(a), radius * math.sin(a), height)
            rot = (90, 0, math.degrees(a) + 90)
        s = size * (1.0 if i % 2 == 0 else 0.7)
        p.add(prism(star_poly(4, s, s * 0.32, 45), 0.012, loc=loc, rot=rot), "glow_blue", False)
    return p.build("Sparkles_Mesh", parent=root, collection=coll)


# --------------------------------------------------------------------------
# Models
# --------------------------------------------------------------------------

def build_squirrel(coll):
    root = empty("Squirrel", coll)

    body = Part()
    body.add(sphere(1, (0, -0.02, 0.0), (0.17, 0.16, 0.21), rot=(-12, 0, 0)), "fur")
    body.add(sphere(1, (0, -0.125, 0.0), (0.12, 0.07, 0.15), rot=(-12, 0, 0), u=12, v=8), "cream")
    for sx in (-1, 1):
        body.add(sphere(1, (sx * 0.085, -0.07, -0.165), (0.06, 0.1, 0.035), u=10, v=6), "fur_dark")
        body.add(sphere(0.045, (sx * 0.06, -0.16, 0.08), u=8, v=6), "fur_dark")      # paws
    body.add(sphere(1, (0, -0.2, 0.085), (0.042, 0.038, 0.052), u=10, v=7), "acorn")  # acorn
    body.add(sphere(1, (0, -0.2, 0.125), (0.048, 0.044, 0.022), u=10, v=6), "fur_dark")  # acorn cap
    body.add(cone(0.006, 0.006, 0.03, 5, loc=(0, -0.2, 0.15)), "fur_dark", False)
    body_obj = body.build("Body", parent=root, pivot=(0, 0, 0.21), collection=coll)

    head = Part()
    head.add(sphere(0.15, (0, -0.04, 0.12)), "fur")
    head.add(sphere(1, (0, -0.155, 0.075), (0.085, 0.065, 0.06), u=12, v=8), "cream")
    head.add(sphere(0.022, (0, -0.218, 0.1), u=8, v=6), "black")                      # nose
    head.add(sphere(1, (0, -0.205, 0.035), (0.032, 0.012, 0.013), u=8, v=5), "black")  # grin
    head.add(box((0.022, 0.01, 0.018), (0, -0.212, 0.022)), "white", False)            # buck teeth
    for sx in (-1, 1):
        head.add(sphere(1, (sx * 0.066, -0.13, 0.155), (0.046, 0.03, 0.056), u=10, v=7), "white")
        head.add(sphere(0.026, (sx * 0.06, -0.153, 0.15), u=8, v=6), "black")
        head.add(sphere(0.009, (sx * 0.052, -0.176, 0.166), u=6, v=4), "white")
        head.add(sphere(1, (sx * 0.072, -0.135, 0.22), (0.04, 0.012, 0.012),          # brows: mischief
                        rot=(0, sx * 18, 0), u=8, v=5), "fur_dark")
        head.add(cone(0.048, 0.0, 0.11, 8, loc=(sx * 0.085, 0.0, 0.265), rot=(0, sx * 20, 0)), "fur")
        head.add(cone(0.028, 0.0, 0.075, 6, loc=(sx * 0.085, -0.022, 0.255), rot=(0, sx * 20, 0)), "pink")
    head.build("Head", parent=body_obj, pivot=(0, -0.035, 0.18), collection=coll)

    # Tail: three-joint chain so the animator can flick it. ~2x real size.
    t1 = Part()
    t1.add(sphere(1, (0, 0.08, 0.1), (0.13, 0.12, 0.17), rot=(-20, 0, 0)), "fur")
    t1_obj = t1.build("Tail_1", parent=body_obj, pivot=(0, 0.15, -0.1), collection=coll)
    t2 = Part()
    t2.add(sphere(1, (0, 0.03, 0.15), (0.15, 0.135, 0.19), rot=(10, 0, 0)), "fur")
    t2.add(sphere(1, (0, 0.11, 0.17), (0.09, 0.06, 0.14), rot=(10, 0, 0), u=10, v=7), "fur_dark")
    t2_obj = t2.build("Tail_2", parent=t1_obj, pivot=(0, 0.13, 0.22), collection=coll)
    t3 = Part()
    t3.add(sphere(1, (0, -0.07, 0.07), (0.14, 0.125, 0.13), rot=(30, 0, 0)), "fur")
    t3.add(sphere(1, (0, -0.17, 0.03), (0.08, 0.07, 0.07), u=10, v=7), "cream")
    t3.build("Tail_3", parent=t2_obj, pivot=(0, 0.0, 0.3), collection=coll)
    return root


def build_pipe(coll):
    root = empty("ConstructionPipe", coll)
    R_OUT, R_IN, HALF = 0.45, 0.36, 0.65
    SEGS = 24
    xs = [-HALF, -0.5] + [(-0.45 + 0.09 * i) for i in range(11)] + [0.5, HALF]
    # Outer tube along X, with diagonal hazard stripes in the middle band.
    bm = bmesh.new()
    rings = []
    for x in xs:
        rings.append([bm.verts.new((x, R_OUT * math.cos(2 * math.pi * i / SEGS),
                                    R_OUT * math.sin(2 * math.pi * i / SEGS))) for i in range(SEGS)])
    face_band = {}
    for k, (a, b) in enumerate(zip(rings, rings[1:])):
        for i in range(SEGS):
            j = (i + 1) % SEGS
            f = bm.faces.new((a[i], a[j], b[j], b[i]))
            face_band[f] = (k, i)
    # Cartoon dent on the top-front.
    for ring in rings[4:8]:
        for v in ring[5:8]:
            v.co.yz *= 0.93
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    stripe_k = range(2, len(xs) - 3)

    def stripe(f):
        k, i = face_band[f]
        if k not in stripe_k:
            return "steel"
        return "gold" if ((i + k) // 3) % 2 == 0 else "black"

    roller = Part()
    roller.add(bm, stripe)
    inner = cone(R_IN, R_IN, 2 * HALF, SEGS, rot=(0, 90, 0), caps=False)
    bmesh.ops.reverse_faces(inner, faces=inner.faces)
    roller.add(inner, "pipe_inside")
    for sx in (-1, 1):
        roller.add(torus(0.405, 0.062, SEGS, 8, loc=(sx * HALF, 0, 0), rot=(0, 90, 0)), "gold")
        for a in range(0, 360, 60):                                      # rim bolts
            r = 0.405
            roller.add(sphere(0.022, (sx * (HALF + 0.05), r * math.cos(math.radians(a)),
                                      r * math.sin(math.radians(a))), u=6, v=4), "steel_dark")
    roller.build("Roller", parent=root, pivot=(0, 0, 0.467), collection=coll)
    return root


def build_frisbee(coll):
    root = empty("StrayFrisbee", coll)
    prof = [(0, 0.045), (0.12, 0.044), (0.2, 0.04), (0.26, 0.033), (0.3, 0.02),
            (0.32, 0.0), (0.315, -0.022), (0.295, -0.034), (0.275, -0.026),
            (0.27, -0.004), (0.15, 0.004), (0, 0.006)]
    disc = lathe(prof, 28)
    p = Part()
    p.add(disc, lambda f: "gold_dark" if f.calc_center_median().length > 0.29 else "gold")
    p.add(prism(star_poly(5, 0.15, 0.065), 0.006, loc=(0, 0, 0.046)), "black", False)
    p.add(prism(V_POLY, 0.008, loc=(0, 0.0, 0.048), scale=(0.11, 0.11, 1)), "gold", False)
    p.build("Disc", parent=root, pivot=(0, 0, 0), collection=coll)
    return root


def question_mark(coll, parent, loc, size=0.22):
    p = Part()
    s = size
    p.add(torus(0.3 * s, 0.09 * s, 14, 6, rot=(90, 0, 0), arc=(-90, 180), loc=(0, 0, 0.45 * s)), "gold")
    p.add(limb((0, 0, 0.15 * s), (0, 0, -0.15 * s), 0.09 * s, segs=8), "gold")
    p.add(sphere(0.11 * s, (0, 0, -0.45 * s), u=8, v=6), "gold")
    return p.build("TellMark", parent=parent, pivot=loc, collection=coll)


def build_freshman(coll):
    root = empty("LostFreshman", coll)
    HIP = 0.5

    for side, sx in (("L", -1), ("R", 1)):
        leg = Part()
        leg.add(limb((0, 0, 0), (0, 0, -0.38), 0.075, 0.065), "denim")
        leg.add(sphere(1, (0, -0.04, -0.43), (0.085, 0.13, 0.06), u=10, v=6), "white")
        leg.add(sphere(1, (0, -0.04, -0.465), (0.088, 0.133, 0.02), u=10, v=4), "black")
        leg.build(f"Leg_{side}", parent=root, pivot=(sx * 0.1, 0, HIP), collection=coll)

    torso = Part()
    torso.add(cone(0.19, 0.16, 0.42, 14, loc=(0, 0, 0.2)), "gold")              # Vandy-gold tee
    torso.add(sphere(1, (0, 0, 0.0), (0.19, 0.16, 0.06), u=14, v=6), "denim")    # waistband
    torso.add(limb((-0.09, -0.12, 0.4), (0.0, -0.17, 0.22), 0.012, segs=5), "black")  # lanyard
    torso.add(limb((0.09, -0.12, 0.4), (0.0, -0.17, 0.22), 0.012, segs=5), "black")
    torso.add(box((0.08, 0.012, 0.1), (0.0, -0.172, 0.16)), "white", False)       # ID badge
    torso.add(sphere(1, (0, 0.22, 0.24), (0.2, 0.14, 0.25), u=12, v=8), "lawn_green")  # backpack
    torso.add(sphere(1, (0, 0.24, 0.43), (0.17, 0.12, 0.08), u=12, v=6), "brick_shadow")  # flap
    torso.add(sphere(1, (0.0, 0.33, 0.18), (0.11, 0.06, 0.1), u=10, v=6), "lawn_green")  # pocket
    for sx in (-1, 1):
        torso.add(limb((sx * 0.12, -0.05, 0.42), (sx * 0.13, 0.12, 0.05), 0.025, segs=6), "black")
    torso_obj = torso.build("Torso", parent=root, pivot=(0, 0, HIP + 0.02), collection=coll)

    head = Part()
    head.add(sphere(0.27, (0, 0, 0.26), u=16, v=10), "skin")
    head.add(sphere(1, (0, 0.04, 0.33), (0.285, 0.28, 0.26), u=16, v=10), "hair")
    for sx in (-1, 1):
        head.add(sphere(1, (sx * 0.265, 0, 0.24), (0.05, 0.035, 0.065), u=8, v=6), "skin")
    head.build("Head", parent=torso_obj, pivot=(0, 0, 0.43), collection=coll)

    # Arms reach forward to hold the map up in front of the face.
    for side, sx in (("L", -1), ("R", 1)):
        arm = Part()
        arm.add(limb((0, 0, 0), (sx * 0.17, -0.36, 0.2), 0.06, 0.05), "skin")
        arm.add(sphere(1, (0, 0, 0), (0.08, 0.08, 0.075), u=10, v=6), "gold")       # sleeve
        arm.add(sphere(0.062, (sx * 0.18, -0.38, 0.21), u=10, v=6), "skin")       # hand
        arm.build(f"Arm_{side}", parent=torso_obj, pivot=(sx * 0.19, 0, 0.38), collection=coll)

    # The map: its own object so it can go flying on a hit.
    m = Part()
    W, H = 0.86, 0.56
    m.add(box((W, 0.014, H)), "map_paper", False)
    for x in (-W / 4, 0, W / 4):
        m.add(box((0.006, 0.018, H - 0.02), (x, 0, 0)), "map_line", False)
    m.add(box((W - 0.02, 0.018, 0.006), (0, 0, 0)), "map_line", False)
    m.add(box((0.14, 0.018, 0.1), (-0.27, 0, -0.14)), "lawn_green", False)     # Alumni Lawn
    m.add(box((0.1, 0.018, 0.08), (0.16, 0, 0.15)), "brick_lit", False)
    route = [(-0.36, -0.2), (-0.2, -0.05), (-0.05, -0.12), (0.08, 0.05), (0.2, -0.02), (0.33, 0.18)]
    for (x1, z1), (x2, z2) in zip(route, route[1:]):
        m.add(limb((x1, -0.011, z1), (x2, -0.011, z2), 0.008, segs=4), "warning_red")
    m.add(sphere(0.025, (0.33, -0.014, 0.18), u=8, v=5), "warning_red")
    m.build("Map", parent=root, pivot=(0, -0.42, HIP + 0.02 + 0.43 + 0.24), collection=coll)

    question_mark(coll, root, (0.0, 0.0, 1.74))
    return root


def build_cookie(coll):
    root = empty("SpeedCookie", coll)
    rnd = random.Random(7)
    prof = [(0, 0.065), (0.12, 0.066), (0.22, 0.062), (0.27, 0.05), (0.3, 0.02),
            (0.302, -0.02), (0.27, -0.05), (0.22, -0.062), (0.12, -0.066), (0, -0.065)]
    disc = lathe(prof, 22)
    for v in disc.verts:                                    # hand-baked lumps
        r = Vector((v.co.x, v.co.y)).length
        if 0.05 < r < 0.28:
            v.co.z += rnd.uniform(-0.008, 0.01) * (1 if v.co.z > 0 else -1)
        elif r >= 0.28:
            k = 1 + rnd.uniform(-0.03, 0.03)
            v.co.x *= k
            v.co.y *= k

    p = Part()
    p.add(disc, lambda f: "cookie_edge" if Vector(f.calc_center_median().xy).length > 0.26 else "cookie")
    # Oversized chocolate chunks on both faces.
    spots = [(0.0, 0.0), (0.14, 0.08), (-0.13, 0.1), (0.05, -0.16), (-0.15, -0.09),
             (0.17, -0.1), (-0.03, 0.19)]
    for face in (1, -1):
        for k, (x, y) in enumerate(spots):
            if face == -1 and k % 2:
                continue
            s = rnd.uniform(0.038, 0.05)
            p.add(cone(s, s * 0.45, 0.04, 5, loc=(x, y, face * 0.068),
                       rot=(0 if face > 0 else 180, 0, rnd.uniform(0, 72))), "chip", False)
    p.add(torus(0.33, 0.018, 28, 6), "glow_blue")           # glow rim
    cookie = p.build("Cookie", parent=root, collection=coll)
    cookie.data.transform(Euler((math.radians(90), 0, 0)).to_matrix().to_4x4())  # on edge, faces -Y
    sparkles(root, coll, radius=0.43, count=3, size=0.06, plane="XZ")
    return root


def build_meal_swipe(coll):
    root = empty("MealSwipe", coll)
    W, H = 0.7, 0.44
    p = Part()
    p.add(prism(rounded_rect(W + 0.07, H + 0.07, 0.06), 0.028), "glow_blue", False)   # glow frame
    p.add(prism(rounded_rect(W, H, 0.045), 0.05), "gold", False)
    p.add(prism(rounded_rect(W - 0.06, H - 0.06, 0.03), 0.056), "black", False)
    for side in (1, -1):
        z = side * 0.0295
        p.add(box((W - 0.06, H * 0.16, 0.004), (0, H * 0.27, z)), "glow_blue", False)  # mag stripe
    # Front (+z here = -Y after rotation): star-and-V + "name" bars.
    p.add(prism(star_poly(5, 0.1, 0.045), 0.006, loc=(-0.19, -0.06, 0.031)), "gold", False)
    p.add(prism(V_POLY, 0.008, loc=(-0.19, -0.065, 0.034), scale=(0.075, 0.075, 1)), "black", False)
    p.add(box((0.26, 0.03, 0.004), (0.12, -0.02, 0.0305)), "white", False)
    p.add(box((0.18, 0.03, 0.004), (0.08, -0.09, 0.0305)), "white", False)
    p.add(box((0.3, 0.06, 0.004), (0.0, -0.09, -0.0305)), "white", False)   # back: signature box
    card = p.build("Card", parent=root, collection=coll)
    card.data.transform(Euler((math.radians(90), 0, 0)).to_matrix().to_4x4())    # upright, front -Y
    sparkles(root, coll, radius=0.5, count=4, size=0.055, plane="XZ")
    return root


MODELS = [
    ("Squirrel", build_squirrel),
    ("ConstructionPipe", build_pipe),
    ("StrayFrisbee", build_frisbee),
    ("LostFreshman", build_freshman),
    ("SpeedCookie", build_cookie),
    ("MealSwipe", build_meal_swipe),
]


# --------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------

def hierarchy(root):
    out = [root]
    for c in root.children:
        out += hierarchy(c)
    return out


def select_only(objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def _claim_clean_names(objs):
    """Blender forces unique names ('Head.001'); Unity scripts look parts up by
    clean name, so give this model's parts their base names before exporting."""
    for o in objs:
        base = o.name.split(".")[0]
        if o.name != base:
            holder = bpy.data.objects.get(base)
            if holder:
                holder.name = base + "_other"
            o.name = base


def export(root, out_dir):
    objs = hierarchy(root)
    _claim_clean_names(objs)
    select_only(objs)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out_dir, f"{root.name}.fbx"),
        use_selection=True, object_types={'EMPTY', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        # bake_space_transform garbles nested hierarchies; Unity's importer does
        # the axis conversion instead (ModelImporter.bakeAxisConversion).
        bake_space_transform=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(out_dir, f"{root.name}.glb"), export_format='GLB',
        use_selection=True, export_apply=True)


def tri_count(root):
    return sum(len(o.data.polygons) for o in hierarchy(root) if o.type == 'MESH')


def main():
    global TEX_DIR
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    out_dir = os.path.abspath(argv[0] if argv else "out")
    tex_dir = os.path.abspath(argv[1] if len(argv) > 1 else out_dir)
    os.makedirs(out_dir, exist_ok=True)
    TEX_DIR = tex_dir

    bpy.ops.wm.read_factory_settings(use_empty=True)
    roots = {}
    for name, fn in MODELS:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
        roots[name] = fn(coll)
    bpy.context.view_layer.update()

    for name, root in roots.items():
        export(root, out_dir)
        print(f"[built] {name:18s} {tri_count(root):5d} tris")

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "Interactables.blend"))


if __name__ == "__main__":
    main()
