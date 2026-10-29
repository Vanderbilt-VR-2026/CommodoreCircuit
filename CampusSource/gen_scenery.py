#!/usr/bin/env python3
"""OSM -> low-poly Alumni Lawn scenery, v2: every piece is its own object.

World frame matches the track: x = east, z = north, y = up (meters).
Origin = average of the Alumni Lawn outline nodes (lat/lon stored in meta).

Output JSON:
  objects[]   one entry per building / road / footpath / lawn (own mesh, pivot at its own center)
  treeMeshes[], trees[]   shared tree shapes + instances (position, yaw, scale, canopy color)
  lampMesh, lamps[]       one shared lamp post + instances
  benchMesh, benches[]    one shared bench + instances
Meshes have one submesh per material ("mat" = material name the Unity builder looks up).
Visual only. Trees/lamps are procedural; building heights are estimates.
"""
import math, json, random, hashlib, sys, re
import xml.etree.ElementTree as ET
import numpy as np
import mapbox_earcut as earcut
from shapely.geometry import Polygon, Point, LineString
from shapely.ops import unary_union
from shapely.prepared import prep

OSM, OUT = sys.argv[1], sys.argv[2]
RADIUS = 270.0
R_EARTH = 6371000.0

# ----------------------------------------------------------------- parse OSM
root = ET.parse(OSM).getroot()
NODES = {n.get("id"): (float(n.get("lat")), float(n.get("lon")), {t.get("k"): t.get("v") for t in n.findall("tag")}) for n in root.findall("node")}
WAYS = {}
for w in root.findall("way"):
    WAYS[w.get("id")] = ([nd.get("ref") for nd in w.findall("nd") if nd.get("ref") in NODES],
                         {t.get("k"): t.get("v") for t in w.findall("tag")})
RELS = [([(m.get("type"), m.get("ref"), m.get("role")) for m in r.findall("member")],
         {t.get("k"): t.get("v") for t in r.findall("tag")}) for r in root.findall("relation")]

lawn_way = next(i for i, (refs, tg) in WAYS.items() if tg.get("name") == "Alumni Lawn")
pts = [NODES[r] for r in WAYS[lawn_way][0]]
LAT0 = sum(p[0] for p in pts) / len(pts)
LON0 = sum(p[1] for p in pts) / len(pts)
KX = R_EARTH * math.radians(1) * math.cos(math.radians(LAT0))
KZ = R_EARTH * math.radians(1)


def proj(lat, lon):
    return ((lon - LON0) * KX, (lat - LAT0) * KZ)


def way_xy(wid):
    return [proj(NODES[r][0], NODES[r][1]) for r in WAYS[wid][0]]


def area2(ring):
    n = len(ring)
    return 0.5 * sum(ring[i][0] * ring[(i + 1) % n][1] - ring[(i + 1) % n][0] * ring[i][1] for i in range(n))


def clean_ring(coords, tol=0.25):
    if len(coords) > 1 and coords[0] == coords[-1]:
        coords = coords[:-1]
    if len(coords) < 3:
        return None
    poly = Polygon(coords)
    if not poly.is_valid:
        poly = poly.buffer(0)
        if poly.geom_type == "MultiPolygon":
            poly = max(poly.geoms, key=lambda g: g.area)
    poly = poly.simplify(tol)
    if poly.is_empty or poly.geom_type != "Polygon" or poly.area < 4:
        return None
    ring = list(poly.exterior.coords)[:-1]
    if area2(ring) < 0:
        ring.reverse()          # always CCW (x right, z up)
    return ring


def safe(s):
    return re.sub(r"[^A-Za-z0-9 _.'&()-]+", "", s).strip()


# ----------------------------------------------------------------- mesh helpers
def normal(a, b, c):
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    return (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)


class Part:
    """vertices + one triangle list per material (flat shading: verts are not shared)"""

    def __init__(self):
        self.v = []
        self.subs = {}

    def tri(self, a, b, c, mat, up=False, out=None):
        n = normal(a, b, c)
        flip = False
        if up and n[1] < 0:
            flip = True
        if out is not None:
            m = ((a[0] + b[0] + c[0]) / 3 - out[0], (a[1] + b[1] + c[1]) / 3 - out[1], (a[2] + b[2] + c[2]) / 3 - out[2])
            if n[0] * m[0] + n[1] * m[1] + n[2] * m[2] < 0:
                flip = True
        if flip:
            b, c = c, b
        i = len(self.v)
        self.v.extend([a, b, c])
        self.subs.setdefault(mat, []).extend([i, i + 1, i + 2])

    def empty(self):
        return not self.v

    def tris(self):
        return sum(len(t) // 3 for t in self.subs.values())

    def dump(self, ox=0.0, oz=0.0):
        return {"verts": [round(c, 2) for v in self.v for c in (v[0] - ox, v[1], v[2] - oz)],
                "subs": [{"mat": m, "tris": t} for m, t in self.subs.items()]}


def flat_poly(part, ring2d, y, mat):
    coords = list(ring2d)
    arr = np.array(coords, dtype=np.float64)
    idx = earcut.triangulate_float64(arr, np.array([len(coords)], dtype=np.uint32))
    for i in range(0, len(idx), 3):
        a, b, c = [(coords[j][0], y, coords[j][1]) for j in idx[i:i + 3]]
        part.tri(a, b, c, mat, up=True)


def box(part, cx, cz, y0, y1, hx, hz, mat, ang=0.0):
    ca, sa = math.cos(ang), math.sin(ang)

    def p(lx, lz, y):
        return (cx + lx * ca - lz * sa, y, cz + lx * sa + lz * ca)

    b = [p(-hx, -hz, y0), p(hx, -hz, y0), p(hx, hz, y0), p(-hx, hz, y0), p(-hx, -hz, y1), p(hx, -hz, y1), p(hx, hz, y1), p(-hx, hz, y1)]
    ctr = (cx, (y0 + y1) / 2, cz)
    for f in [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        part.tri(b[f[0]], b[f[1]], b[f[2]], mat, out=ctr)
        part.tri(b[f[0]], b[f[2]], b[f[3]], mat, out=ctr)


def ribbon(part, pts, w, y, mat):
    n = len(pts)
    left, right = [], []
    for i in range(n):
        if i == 0:
            d = (pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]); m = 1.0
        elif i == n - 1:
            d = (pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]); m = 1.0
        else:
            d1 = (pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1])
            d2 = (pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
            l1, l2 = math.hypot(*d1) or 1, math.hypot(*d2) or 1
            d = (d1[0] / l1 + d2[0] / l2, d1[1] / l1 + d2[1] / l2)
            if math.hypot(*d) < 0.3:
                d = d1
            dn = (d[0] / (math.hypot(*d) or 1), d[1] / (math.hypot(*d) or 1))
            cosang = dn[0] * (d1[0] / l1) + dn[1] * (d1[1] / l1)
            m = 1.0 / max(cosang, 0.5)
        dl = math.hypot(*d) or 1
        nx, nz = -d[1] / dl, d[0] / dl
        left.append((pts[i][0] + nx * w / 2 * m, y, pts[i][1] + nz * w / 2 * m))
        right.append((pts[i][0] - nx * w / 2 * m, y, pts[i][1] - nz * w / 2 * m))
    for i in range(n - 1):
        part.tri(left[i], left[i + 1], right[i], mat, up=True)
        part.tri(right[i], left[i + 1], right[i + 1], mat, up=True)


def hsh(s, n):
    return int(hashlib.md5(str(s).encode()).hexdigest(), 16) % n


OBJECTS = []       # {"name","group","x","z", mesh...}
used_names = set()


def uniq(name):
    base, k = name, 2
    while name in used_names:
        name = f"{base} ({k})"; k += 1
    used_names.add(name)
    return name


def emit(group, name, part, ox, oz):
    if part.empty():
        return
    o = {"name": uniq(name), "group": group, "x": round(ox, 2), "z": round(oz, 2)}
    o.update(part.dump(ox, oz))
    OBJECTS.append(o)


# ----------------------------------------------------------------- features
BUILD = []
for wid, (refs, tg) in WAYS.items():
    if "building" in tg and len(refs) >= 4 and refs[0] == refs[-1]:
        ring = clean_ring(way_xy(wid))
        if ring:
            BUILD.append({"id": wid, "ring": ring, "tags": tg})
for mem, tg in RELS:
    if "building" in tg:
        for typ, ref, role in mem:
            if typ == "way" and role == "outer" and ref in WAYS and not any(b["id"] == ref for b in BUILD):
                refs = WAYS[ref][0]
                if len(refs) >= 4 and refs[0] == refs[-1]:
                    ring = clean_ring(way_xy(ref))
                    if ring:
                        BUILD.append({"id": ref, "ring": ring, "tags": {**tg, **WAYS[ref][1]}})
BUILD = [b for b in BUILD if math.hypot(*Polygon(b["ring"]).centroid.coords[0]) < RADIUS]
print("buildings in radius:", len(BUILD))

circle = Point(0, 0).buffer(RADIUS, 32)
LAWNS, BEDS = [], []
for wid, (refs, tg) in WAYS.items():
    if len(refs) < 4 or refs[0] != refs[-1]:
        continue
    if tg.get("landuse") == "flowerbed":
        ring = clean_ring(way_xy(wid), 0.2)
        if ring and Polygon(ring).intersects(circle):
            BEDS.append({"ring": ring, "id": wid, "name": tg.get("name", "")})
    elif (tg.get("leisure") in ("park", "garden") and tg.get("name") != "Centennial Park") or tg.get("landuse") == "grass":
        ring = clean_ring(way_xy(wid), 0.3)
        if ring and Polygon(ring).intersects(circle):
            LAWNS.append({"ring": ring, "name": tg.get("name", ""), "id": wid})
print("lawns:", [(l["name"], round(Polygon(l["ring"]).area)) for l in LAWNS], "beds:", len(BEDS))

# ---- clipped polyline pieces
bpoly = unary_union([Polygon(b["ring"]).buffer(-0.6) for b in BUILD])
bprep = prep(bpoly)


def pieces_of(wid, avoid_buildings):
    out, seg = [], []
    for p in way_xy(wid):
        ok = math.hypot(*p) < RADIUS and not (avoid_buildings and bprep.contains(Point(p)))
        if ok:
            seg.append(p)
        else:
            if len(seg) >= 2:
                out.append(seg)
            seg = []
    if len(seg) >= 2:
        out.append(seg)
    return out


PATH_W = {"footway": 2.6, "steps": 2.2, "path": 2.0, "pedestrian": 3.5, "cycleway": 2.6}
ROAD_W = {"primary": 11.0, "secondary": 9.5, "tertiary": 8.5, "residential": 7.0, "unclassified": 7.0, "service": 4.5}
ROAD_Y = {"primary": 0.050, "secondary": 0.049, "tertiary": 0.048, "residential": 0.047, "unclassified": 0.047, "service": 0.046}
Y_BED, Y_LAWN, Y_PATH = 0.04, 0.02, 0.07

PATHS, ROADS = [], []
for wid, (refs, tg) in WAYS.items():
    hw = tg.get("highway")
    if hw in PATH_W:
        if tg.get("footway") == "crossing" or tg.get("indoor") or tg.get("tunnel") == "yes" or tg.get("area") == "yes":
            continue
        for k, s in enumerate(pieces_of(wid, True)):
            PATHS.append({"id": wid, "k": k, "pts": s, "w": PATH_W[hw], "hw": hw, "name": tg.get("name", "")})
    elif hw in ROAD_W:
        if tg.get("tunnel") == "yes" or tg.get("area") == "yes":
            continue
        for k, s in enumerate(pieces_of(wid, False)):
            ROADS.append({"id": wid, "k": k, "pts": s, "w": ROAD_W[hw], "hw": hw, "name": tg.get("name", "")})
print("path pieces:", len(PATHS), "road pieces:", len(ROADS))

path_lines = [LineString(p["pts"]) for p in PATHS]
path_all = unary_union(path_lines) if path_lines else None
road_union = unary_union([LineString(r["pts"]).buffer(r["w"] / 2 + 1.0) for r in ROADS]) if ROADS else Point(0, 0).buffer(0.01)
road_prep = prep(road_union)

# ----------------------------------------------------------------- emit lawns / paths / roads
for l in LAWNS:
    c = Polygon(l["ring"]).centroid
    p = Part()
    flat_poly(p, l["ring"], Y_LAWN, "Lawn")
    emit("Lawns", l["name"] or f"Lawn_{l['id']}", p, c.x, c.y)
for b in BEDS:
    c = Polygon(b["ring"]).centroid
    p = Part()
    flat_poly(p, b["ring"], Y_BED, "Flowerbed")
    emit("Lawns", b["name"] or f"Flowerbed_{b['id']}", p, c.x, c.y)

for pc in PATHS:
    ls = LineString(pc["pts"])
    c = ls.centroid
    p = Part()
    ribbon(p, pc["pts"], pc["w"], Y_PATH, "Pavement")
    label = "Steps" if pc["hw"] == "steps" else "Path"
    emit("Footpaths", f"{label}_{pc['id']}_{pc['k']}", p, c.x, c.y)

for rd in ROADS:
    ls = LineString(rd["pts"])
    c = ls.centroid
    p = Part()
    y = ROAD_Y[rd["hw"]]
    ribbon(p, rd["pts"], rd["w"], y, "Asphalt")
    if rd["hw"] in ("primary", "secondary", "tertiary") and ls.length > 12:
        d = 2.0
        while d + 3.0 < ls.length:
            a, bb = ls.interpolate(d), ls.interpolate(d + 3.0)
            dx, dz = bb.x - a.x, bb.y - a.y
            dl = math.hypot(dx, dz) or 1
            nx, nz = -dz / dl * 0.08, dx / dl * 0.08
            q = [(a.x + nx, y + 0.012, a.y + nz), (a.x - nx, y + 0.012, a.y - nz), (bb.x - nx, y + 0.012, bb.y - nz), (bb.x + nx, y + 0.012, bb.y + nz)]
            p.tri(q[0], q[1], q[2], "RoadLine", up=True)
            p.tri(q[0], q[2], q[3], "RoadLine", up=True)
            d += 9.0
    nm = rd["name"] or rd["hw"].capitalize()
    emit("Roads", f"Road_{safe(nm)}_{rd['id']}_{rd['k']}", p, c.x, c.y)

# ----------------------------------------------------------------- buildings (one object each)
STONE_NAMES = {"Kirkland Hall", "Old Central", "Neely Auditorium", "Alumni Hall", "Benson Science Hall", "Historic Old Gym",
               "Bryan Building", "Buttrick Hall", "St. Augustine's Episcopal Chapel", "Rand Hall", "Sarratt Student Center"}
DEFAULT_H = {"house": 7, "residential": 15, "university": 14, "yes": 11, "church": 12, "commercial": 9, "retail": 6,
             "hospital": 22, "parking": 12, "hotel": 25, "grandstand": 8, "roof": 4}
NAME_H = {"Kirkland Hall": 19, "Alumni Hall": 14, "Tolman Hall": 17, "Cole Hall": 17, "McGill Hall": 17, "Old Central": 14,
          "Neely Auditorium": 13, "Benson Science Hall": 17, "Buttrick Hall": 19, "Sarratt Student Center": 12,
          "Rand Hall": 13, "Historic Old Gym": 13, "Bryan Building": 14}
bstats = {"gable": 0, "flat": 0, "windows": 0, "walls": 0}
bad_walls = 0

for b in BUILD:
    tg, ring = b["tags"], b["ring"]
    poly = Polygon(ring)
    name = tg.get("name", "")
    kind = tg.get("building", "yes")
    h = None
    if "height" in tg:
        try: h = float(tg["height"].split()[0])
        except Exception: h = None
    if h is None and "building:levels" in tg:
        try: h = float(tg["building:levels"]) * 3.5 + 1.0
        except Exception: h = None
    if h is None:
        h = NAME_H.get(name, DEFAULT_H.get(kind, 10) + (hsh(b["id"], 7) - 3) * 0.6)
    if poly.area < 40 and h > 8:
        h = 5
    if name in STONE_NAMES:
        wm = "Limestone"
    elif kind == "house":
        wm = ["Brick", "WhitePaint", "TanBrick"][hsh(b["id"], 3)]
    else:
        wm = "TanBrick" if hsh(b["id"], 5) < 2 else "Brick"
    cen = poly.centroid
    part = Part()
    n = len(ring)
    floors = max(1, min(8, round((h - 1.0) / 3.5)))
    near = math.hypot(cen.x, cen.y) < 230
    for i in range(n):
        p0, p1 = ring[i], ring[(i + 1) % n]
        dx, dz = p1[0] - p0[0], p1[1] - p0[1]
        L = math.hypot(dx, dz)
        if L < 0.2:
            continue
        a, bq = (p0[0], 0.0, p0[1]), (p1[0], 0.0, p1[1])
        c, d = (p1[0], h, p1[1]), (p0[0], h, p0[1])
        ox, oz = dz / L, -dx / L      # outward for a CCW ring
        # winding is fixed (NOT guessed from the building center, which breaks L-shaped footprints)
        part.tri(a, c, bq, wm)
        part.tri(a, d, c, wm)
        bstats["walls"] += 1
        # sanity: the wall normal must point outward
        t0 = normal(a, c, bq)
        if t0[0] * ox + t0[2] * oz <= 0:
            bad_walls += 1
        if near and L >= 5.0 and h >= 5.0:
            cnt = int((L - 1.6) // 3.2)
            for f in range(floors if cnt >= 1 else 0):
                y0 = 1.0 + f * 3.5
                if y0 + 1.6 > h:
                    break
                for k in range(cnt):
                    t = (k + 0.5) / cnt
                    cx, cz = p0[0] + dx * t, p0[1] + dz * t
                    ux, uz = dx / L * 0.65, dz / L * 0.65
                    e = 0.05
                    wa = (cx - ux + ox * e, y0, cz - uz + oz * e)
                    wb = (cx + ux + ox * e, y0, cz + uz + oz * e)
                    wc = (cx + ux + ox * e, y0 + 1.6, cz + uz + oz * e)
                    wd = (cx - ux + ox * e, y0 + 1.6, cz - uz + oz * e)
                    part.tri(wa, wc, wb, "Windows")
                    part.tri(wa, wd, wc, "Windows")
                    bstats["windows"] += 1
    # roof
    obb = poly.minimum_rotated_rectangle
    fill = poly.area / obb.area if obb.area > 0 else 0
    gable = fill > 0.82 and poly.area < 650 and kind in ("house", "residential", "church", "yes", "university", "roof", "commercial", "retail") and name != "Kirkland Hall" and h < 20
    if gable:
        oc = list(obb.exterior.coords)[:4]
        e1 = (oc[1][0] - oc[0][0], oc[1][1] - oc[0][1])
        e2 = (oc[2][0] - oc[1][0], oc[2][1] - oc[1][1])
        l1, l2 = math.hypot(*e1), math.hypot(*e2)
        if l1 >= l2:
            u, a_len, b_len = (e1[0] / l1, e1[1] / l1), l1 / 2, l2 / 2
        else:
            u, a_len, b_len = (e2[0] / l2, e2[1] / l2), l2 / 2, l1 / 2
        vv = (-u[1], u[0])
        C = (obb.centroid.x, obb.centroid.y)
        A_, B_ = a_len + 0.45, b_len + 0.45
        ridge = h + min(max(b_len * 0.55, 1.2), 4.5)

        def P(su, sv, y):
            return (C[0] + u[0] * A_ * su + vv[0] * B_ * sv, y, C[1] + u[1] * A_ * su + vv[1] * B_ * sv)

        E1, E2, E3, E4 = P(1, 1, h), P(-1, 1, h), P(-1, -1, h), P(1, -1, h)
        R1, R2 = P(1, 0, ridge), P(-1, 0, ridge)
        ctr = (C[0], h - 1.5, C[1])      # roof prism is convex, so a center test is safe here
        for tri_ in [(E1, E2, R2), (E1, R2, R1), (E3, E4, R1), (E3, R1, R2), (E1, R1, E4), (E2, E3, R2)]:
            part.tri(*tri_, "RoofSlate", out=ctr)
        flat_poly(part, ring, h - 0.05, "RoofFlat")
        bstats["gable"] += 1
    else:
        flat_poly(part, ring, h, "RoofFlat")
        bstats["flat"] += 1
    if name == "Kirkland Hall":
        box(part, cen.x, cen.y, h, h + 14, 3.2, 3.2, "Limestone", 0)
        top = h + 14
        ap = (cen.x, top + 8.5, cen.y)
        cs = [(cen.x - 3.8, top, cen.y - 3.8), (cen.x + 3.8, top, cen.y - 3.8), (cen.x + 3.8, top, cen.y + 3.8), (cen.x - 3.8, top, cen.y + 3.8)]
        for i in range(4):
            part.tri(cs[i], cs[(i + 1) % 4], ap, "RoofSlate", out=(cen.x, top - 1, cen.y))
        part.tri(cs[0], cs[1], cs[2], "RoofSlate", out=(cen.x, top + 2, cen.y))
        part.tri(cs[0], cs[2], cs[3], "RoofSlate", out=(cen.x, top + 2, cen.y))
    emit("Buildings", name if name else f"Building_{b['id']}", part, cen.x, cen.y)
print("building stats:", bstats, "wall faces pointing inward:", bad_walls)

# ----------------------------------------------------------------- trees / lamps / benches
rng = random.Random(7)
build_buf = unary_union([Polygon(b["ring"]).buffer(3.0) for b in BUILD])
build_prep = prep(build_buf)
path_buf = path_all.buffer(1.7) if path_all is not None else Point(0, 0).buffer(0.01)
path_buf_prep = prep(path_buf)
TREES = []


def tree_ok(x, z, mind=7.0):
    if math.hypot(x, z) > RADIUS - 8:
        return False
    p = Point(x, z)
    if build_prep.contains(p) or path_buf_prep.contains(p) or road_prep.contains(p):
        return False
    return all((tx - x) ** 2 + (tz - z) ** 2 >= mind * mind for tx, tz, _ in TREES)


for n in NODES.values():
    if n[2].get("natural") == "tree":
        x, z = proj(n[0], n[1])
        if math.hypot(x, z) < RADIUS - 8:
            TREES.append((x, z, 1.0 + rng.random() * 0.3))


def perimeter_trees(ring, inset, spacing, jitter=0.35):
    inner = Polygon(ring).buffer(-inset)
    if inner.is_empty:
        return
    for g in ([inner] if inner.geom_type == "Polygon" else list(inner.geoms)):
        line = g.exterior
        d = rng.random() * spacing
        while d < line.length:
            pt = line.interpolate(d)
            x, z = pt.x + rng.uniform(-1.5, 1.5), pt.y + rng.uniform(-1.5, 1.5)
            if tree_ok(x, z, 8.0):
                TREES.append((x, z, rng.uniform(0.9, 1.45)))
            d += spacing * (1 + rng.uniform(-jitter, jitter))


for l in LAWNS:
    area = Polygon(l["ring"]).area
    if l["name"] == "Alumni Lawn":
        perimeter_trees(l["ring"], 4.0, 11.0)
        poly = Polygon(l["ring"]).buffer(-14)
        if not poly.is_empty:
            minx, minz, maxx, maxz = poly.bounds
            k = 0
            for _ in range(400):
                x, z = rng.uniform(minx, maxx), rng.uniform(minz, maxz)
                if poly.contains(Point(x, z)) and tree_ok(x, z, 28.0):
                    TREES.append((x, z, rng.uniform(1.2, 1.6)))
                    k += 1
                    if k >= 6:
                        break
    elif area > 400:
        perimeter_trees(l["ring"], 3.5, 18.0)

for pc in PATHS:
    line = LineString(pc["pts"])
    L = line.length
    if L < 25:
        continue
    d, side = rng.uniform(5, 15), 1
    while d < L:
        pt, pt2 = line.interpolate(d), line.interpolate(min(L, d + 1.0))
        dx, dz = pt2.x - pt.x, pt2.y - pt.y
        dl = math.hypot(dx, dz) or 1
        off = 5.0 + rng.uniform(0, 2.5)
        x, z = pt.x - dz / dl * off * side, pt.y + dx / dl * off * side
        if rng.random() < 0.55 and tree_ok(x, z, 9.0):
            TREES.append((x, z, rng.uniform(0.85, 1.4)))
        side = -side
        d += rng.uniform(22, 38)
for _ in range(600):
    if len(TREES) >= 300:
        break
    ang, rad = rng.uniform(0, 2 * math.pi), RADIUS * math.sqrt(rng.random())
    x, z = rad * math.cos(ang), rad * math.sin(ang)
    if tree_ok(x, z, 16.0):
        TREES.append((x, z, rng.uniform(0.8, 1.3)))
print("trees:", len(TREES))


def ico(sub=1):
    t = (1 + math.sqrt(5)) / 2
    v = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    v = [tuple(c / math.sqrt(sum(q * q for q in p)) for c in p) for p in v]
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    for _ in range(sub):
        cache, nf = {}, []

        def mid(a, b):
            k = (min(a, b), max(a, b))
            if k not in cache:
                m = tuple((v[a][i] + v[b][i]) / 2 for i in range(3))
                l = math.sqrt(sum(q * q for q in m))
                v.append(tuple(q / l for q in m))
                cache[k] = len(v) - 1
            return cache[k]

        for a, b, c in f:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            nf += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        f = nf
    return v, f


ICO_V, ICO_F = ico(1)
N_TREE_VARIANTS = 6
TREE_MESHES = []
for vi in range(N_TREE_VARIANTS):
    vr = random.Random(100 + vi)
    p = Part()
    H = 10.0
    trunk_h, tr = H * 0.38, 0.30
    rb = [(tr * math.cos(k * math.pi / 3), 0.0, tr * math.sin(k * math.pi / 3)) for k in range(6)]
    rt = [(tr * 0.7 * math.cos(k * math.pi / 3), trunk_h, tr * 0.7 * math.sin(k * math.pi / 3)) for k in range(6)]
    ct = (0, trunk_h / 2, 0)
    for k in range(6):
        k2 = (k + 1) % 6
        p.tri(rb[k], rb[k2], rt[k2], "Trunk", out=ct)
        p.tri(rb[k], rt[k2], rt[k], "Trunk", out=ct)
    cr = 4.6
    cy = trunk_h + cr * 0.55
    sx, sy = 1.0 + vr.uniform(-0.12, 0.12), 0.78 + vr.uniform(-0.08, 0.10)
    jit = [1.0 + vr.uniform(-0.13, 0.13) for _ in ICO_V]
    pv = [(vx * cr * j * sx, cy + vy * cr * j * sy, vz * cr * j * sx) for (vx, vy, vz), j in zip(ICO_V, jit)]
    cc = (0, cy, 0)
    for a, b, c in ICO_F:
        p.tri(pv[a], pv[b], pv[c], "Canopy", out=cc)   # canopy color is chosen per instance in Unity
    d = p.dump()
    d["name"] = f"TreeShape_{vi}"
    TREE_MESHES.append(d)

TREE_INST = []
for x, z, s in TREES:
    pick = rng.random()
    TREE_INST.append({"x": round(x, 2), "z": round(z, 2), "rot": round(rng.uniform(0, 360), 1), "scale": round(s, 2),
                      "mesh": rng.randrange(N_TREE_VARIANTS), "canopy": 2 if pick < 0.12 else (0 if pick < 0.6 else 1)})

# lamp post (one shared mesh)
lp = Part()
box(lp, 0, 0, 0, 4.2, 0.07, 0.07, "LampPost")
box(lp, 0, 0, 0, 0.5, 0.16, 0.16, "LampPost")
box(lp, 0, 0, 4.2, 4.65, 0.22, 0.22, "LampHead")
LAMP_MESH = lp.dump()
LAMP_MESH["name"] = "LampPostShape"

LAMPS = []
for pc in PATHS:
    if pc["hw"] not in ("footway", "pedestrian"):
        continue
    line = LineString(pc["pts"])
    L = line.length
    d, side = rng.uniform(4, 12), 1
    while d < L:
        pt = line.interpolate(d)
        if math.hypot(pt.x, pt.y) < 150:
            pt2 = line.interpolate(min(L, d + 1.0))
            dx, dz = pt2.x - pt.x, pt2.y - pt.y
            dl = math.hypot(dx, dz) or 1
            off = pc["w"] / 2 + 0.9
            x, z = pt.x - dz / dl * off * side, pt.y + dx / dl * off * side
            q = Point(x, z)
            if not bpoly.contains(q) and not road_prep.contains(q):
                LAMPS.append({"x": round(x, 2), "z": round(z, 2), "rot": 0, "scale": 1, "mesh": 0, "canopy": 0})
            side = -side
        d += 32
print("lamps:", len(LAMPS))

# bench (one shared mesh; seat long axis = local X, backrest on local +Z)
bp = Part()
box(bp, 0, 0, 0.40, 0.46, 0.85, 0.22, "Bench")
box(bp, 0, 0.20, 0.46, 0.95, 0.85, 0.04, "Bench")
box(bp, -0.65, 0, 0, 0.40, 0.05, 0.18, "Bench")
box(bp, 0.65, 0, 0, 0.40, 0.05, 0.18, "Bench")
BENCH_MESH = bp.dump()
BENCH_MESH["name"] = "BenchShape"
BENCHES = []
for n in NODES.values():
    if n[2].get("amenity") == "bench":
        x, z = proj(n[0], n[1])
        if math.hypot(x, z) > RADIUS - 5:
            continue
        yaw = rng.uniform(0, 360)
        if path_all is not None:
            q = path_all.interpolate(path_all.project(Point(x, z)))
            vx, vz = q.x - x, q.y - z
            if math.hypot(vx, vz) > 0.01:
                th = -math.atan2(vx, -vz)          # Unity yaw so local X runs parallel to the path
                # backrest (local +Z) should face away from the path
                if (math.sin(th) * -vx + math.cos(th) * -vz) < 0:
                    th += math.pi
                yaw = math.degrees(th) % 360
        BENCHES.append({"x": round(x, 2), "z": round(z, 2), "rot": round(yaw, 1), "scale": 1, "mesh": 0, "canopy": 0})
print("benches:", len(BENCHES))

# ----------------------------------------------------------------- output
meta = {"originLat": LAT0, "originLon": LON0, "radius": RADIUS,
        "credit": "Map data (c) OpenStreetMap contributors, ODbL",
        "buildings": len(BUILD), "trees": len(TREE_INST), "lamps": len(LAMPS), "benches": len(BENCHES),
        "roads": len(ROADS), "alumniLawn": None}
lawn_poly = None
for l in LAWNS:
    if l["name"] == "Alumni Lawn":
        lawn_poly = Polygon(l["ring"])
        meta["alumniLawn"] = {"area": lawn_poly.area, "centroid": [lawn_poly.centroid.x, lawn_poly.centroid.y], "bounds": list(lawn_poly.bounds)}
cam = None
if lawn_poly is not None:
    lc = meta["alumniLawn"]["centroid"]
    cands = []
    for ang in range(0, 360, 15):
        for dist in (10, 16, 22, 30):
            x, z = lc[0] + dist * math.cos(math.radians(ang)), lc[1] + dist * math.sin(math.radians(ang))
            p = Point(x, z)
            if not lawn_poly.contains(p) or build_prep.contains(p) or path_buf_prep.contains(p):
                continue
            if any((tx - x) ** 2 + (tz - z) ** 2 < 36 for tx, tz, _ in TREES):
                continue
            cands.append((-abs(((ang - 270 + 180) % 360) - 180) + dist * 0.5, x, z))
    if cands:
        cands.sort(reverse=True)
        _, cx_, cz_ = cands[0]
        cam = ([round(cx_, 2), 1.8, round(cz_, 2)], [round(lc[0], 2), 3.0, round(lc[1], 2)])
meta["cameraPos"] = cam[0] if cam else [0.0, 1.8, -30.0]
meta["cameraTarget"] = cam[1] if cam else [0.0, 3.0, 0.0]

tot = sum(sum(len(s["tris"]) // 3 for s in o["subs"]) for o in OBJECTS)
tot += sum(sum(len(s["tris"]) // 3 for s in m["subs"]) * 1 for m in [TREE_MESHES[0]]) * len(TREE_INST)
tot += sum(len(s["tris"]) // 3 for s in LAMP_MESH["subs"]) * len(LAMPS) + sum(len(s["tris"]) // 3 for s in BENCH_MESH["subs"]) * len(BENCHES)
meta["totalTris"] = tot
groups = {}
for o in OBJECTS:
    groups[o["group"]] = groups.get(o["group"], 0) + 1
print("objects per group:", groups, "approx total tris:", tot)

out = {"meta": meta, "ground": {"center": [0.0, 0.0], "size": [RADIUS * 2 + 80, RADIUS * 2 + 80]},
       "objects": OBJECTS, "treeMeshes": TREE_MESHES, "trees": TREE_INST,
       "lampMesh": LAMP_MESH, "lamps": LAMPS, "benchMesh": BENCH_MESH, "benches": BENCHES}
json.dump(out, open(OUT, "w"), separators=(",", ":"))
print("wrote", OUT)
json.dump({"buildings": [b["ring"] for b in BUILD], "lawns": [l["ring"] for l in LAWNS],
           "roads": [(r["pts"], r["w"]) for r in ROADS], "paths": [p["pts"] for p in PATHS], "trees": [(t[0], t[1]) for t in TREES]},
          open(OUT.replace(".json", "_debug.json"), "w"))
