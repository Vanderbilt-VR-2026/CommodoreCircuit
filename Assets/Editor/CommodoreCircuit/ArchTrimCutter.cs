using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Tifa's Carmichael model wraps each wing in a 0.8 m limestone base trim, and the trim runs
    /// straight across the bottom of both drive-through arches: a strip across the road at each end
    /// of the tunnel. This cuts the trim out wherever the wall right behind it is open.
    ///
    /// Runs on every import of the FBX (see EnvironmentModelImport), so the FBX itself is never changed.
    /// The model's walls are axis-aligned, so it works in the mesh's own axes, whichever way is up.
    /// </summary>
    public static class ArchTrimCutter
    {
        private const float TrimTop = 0.85f;      // the trim is 0.8 m tall
        private const float WallReach = 0.35f;    // the trim sits 0.1 m proud of its wall
        private const float SampleHeight = 0.4f;  // look for the wall halfway up the trim
        private const float Step = 0.05f;
        private const float MinOpening = 1f;      // ignore the trim's own overhang at the corners

        private struct Face { public int sub, first, normalAxis, alongAxis; public float plane, min, max; }

        // Returns how many openings were cut.
        public static int Cut(Mesh mesh)
        {
            // Up is the axis the building stands on: one end of its range sits at ground level.
            Bounds bounds = mesh.bounds;
            int up = 0;
            for (int k = 1; k < 3; k++) if (Ground(bounds, k) < Ground(bounds, up)) up = k;

            var pos = new List<Vector3>(); mesh.GetVertices(pos);
            var nrm = new List<Vector3>(); mesh.GetNormals(nrm);
            var tan = new List<Vector4>(); mesh.GetTangents(tan);
            var col = new List<Color>(); mesh.GetColors(col);
            var uvs = new List<Vector2>[8];
            for (int c = 0; c < 8; c++) { uvs[c] = new List<Vector2>(); mesh.GetUVs(c, uvs[c]); }

            var tris = new List<int>[mesh.subMeshCount];
            var trim = new List<Face>();
            var walls = new List<Face>();
            for (int s = 0; s < tris.Length; s++)
            {
                tris[s] = new List<int>(mesh.GetTriangles(s));
                for (int t = 0; t < tris[s].Count; t += 3)
                {
                    if (!Vertical(pos, tris[s], t, up, out Face f)) continue;
                    f.sub = s; f.first = t;
                    float top = 0f, bottom = float.MaxValue;
                    for (int k = 0; k < 3; k++)
                    {
                        float h = Mathf.Abs(pos[tris[s][t + k]][up]);
                        top = Mathf.Max(top, h); bottom = Mathf.Min(bottom, h);
                    }
                    if (top <= TrimTop && bottom < 0.01f && f.max - f.min > 3f) trim.Add(f);
                    else walls.Add(f);
                }
            }

            var cuts = new HashSet<Vector3Int>();
            var remove = new HashSet<(int, int)>();
            foreach (Face f in trim)
            {
                List<Vector2> openings = Openings(f, walls, pos, tris, up);
                if (openings.Count == 0) continue;

                var pieces = new List<List<int>> { new List<int> { tris[f.sub][f.first], tris[f.sub][f.first + 1], tris[f.sub][f.first + 2] } };
                foreach (Vector2 o in openings)
                {
                    cuts.Add(new Vector3Int(f.normalAxis, Mathf.RoundToInt(f.plane * 10f), Mathf.RoundToInt(o.x * 10f)));
                    var kept = new List<List<int>>();
                    foreach (List<int> poly in pieces)
                    {
                        kept.Add(Clip(poly, f.alongAxis, o.x, true, pos, nrm, tan, col, uvs));
                        kept.Add(Clip(poly, f.alongAxis, o.y, false, pos, nrm, tan, col, uvs));
                    }
                    pieces = kept;
                }
                remove.Add((f.sub, f.first));
                foreach (List<int> poly in pieces)
                    for (int k = 1; k + 1 < poly.Count; k++)
                        tris[f.sub].AddRange(new[] { poly[0], poly[k], poly[k + 1] });
            }
            if (cuts.Count == 0) return 0;

            // Drop the original trim triangles (back to front, so earlier indices stay valid).
            var ordered = new List<(int sub, int first)>(remove);
            ordered.Sort((a, b) => b.first.CompareTo(a.first));
            foreach (var (sub, first) in ordered) tris[sub].RemoveRange(first, 3);

            if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            if (nrm.Count == pos.Count) mesh.SetNormals(nrm);
            if (tan.Count == pos.Count) mesh.SetTangents(tan);
            if (col.Count == pos.Count) mesh.SetColors(col);
            for (int c = 0; c < 8; c++) if (uvs[c].Count == pos.Count) mesh.SetUVs(c, uvs[c]);
            for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s, false);
            mesh.RecalculateBounds();
            return cuts.Count;
        }

        private static float Ground(Bounds b, int axis) => Mathf.Min(Mathf.Abs(b.min[axis]), Mathf.Abs(b.max[axis]));

        // An axis-aligned vertical triangle: flat along one horizontal axis, spread along the other.
        private static bool Vertical(List<Vector3> pos, List<int> tris, int t, int up, out Face f)
        {
            f = default;
            Vector3 a = pos[tris[t]], b = pos[tris[t + 1]], c = pos[tris[t + 2]];
            float height = Mathf.Max(a[up], b[up], c[up]) - Mathf.Min(a[up], b[up], c[up]);
            if (height < 0.1f) return false;
            for (int n = 0; n < 3; n++)
            {
                if (n == up) continue;
                if (Mathf.Max(a[n], b[n], c[n]) - Mathf.Min(a[n], b[n], c[n]) > 0.01f) continue;
                f.normalAxis = n;
                f.alongAxis = 3 - n - up;
                f.plane = a[n];
                f.min = Mathf.Min(a[f.alongAxis], b[f.alongAxis], c[f.alongAxis]);
                f.max = Mathf.Max(a[f.alongAxis], b[f.alongAxis], c[f.alongAxis]);
                return true;
            }
            return false;
        }

        // Stretches of the trim with no wall behind it, but wall on both sides (an archway, not the trim's end).
        private static List<Vector2> Openings(Face f, List<Face> walls, List<Vector3> pos, List<int>[] tris, int up)
        {
            var behind = walls.FindAll(w => w.normalAxis == f.normalAxis && Mathf.Abs(w.plane - f.plane) <= WallReach &&
                                            w.max > f.min && w.min < f.max);
            var result = new List<Vector2>();
            bool seenWall = false;
            float gapStart = 0f;
            bool inGap = false;
            for (float s = f.min; s <= f.max; s += Step)
            {
                bool covered = behind.Exists(w => Covers(w, s, pos, tris[w.sub], f.alongAxis, up));
                if (!covered && seenWall && !inGap) { inGap = true; gapStart = s; }
                if (covered && inGap)
                {
                    inGap = false;
                    // Each edge lies between the last covered sample and the first open one (or the reverse).
                    if (s - gapStart >= MinOpening)
                        result.Add(new Vector2(Snap(gapStart - Step * 0.5f, behind, pos, tris, f.alongAxis),
                                               Snap(s - Step * 0.5f, behind, pos, tris, f.alongAxis)));
                }
                seenWall |= covered;
            }
            return result;
        }

        private static bool Covers(Face w, float along, List<Vector3> pos, List<int> tris, int alongAxis, int up)
        {
            Vector2 p = new Vector2(along, SampleHeight);
            Vector2 V(int k) { Vector3 v = pos[tris[w.first + k]]; return new Vector2(v[alongAxis], Mathf.Abs(v[up])); }
            Vector2 a = V(0), b = V(1), c = V(2);
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // Line the cut up with the wall's own edge (the arch's pier), found within a sample step or so.
        private static float Snap(float along, List<Face> walls, List<Vector3> pos, List<int>[] tris, int alongAxis)
        {
            float best = along, bestDist = Step;
            foreach (Face w in walls)
                for (int k = 0; k < 3; k++)
                {
                    float d = Mathf.Abs(pos[tris[w.sub][w.first + k]][alongAxis] - along);
                    if (d < bestDist) { bestDist = d; best = pos[tris[w.sub][w.first + k]][alongAxis]; }
                }
            return best;
        }

        // Sutherland-Hodgman against one axis-aligned plane; new corners blend every vertex attribute.
        private static List<int> Clip(List<int> poly, int axis, float at, bool keepBelow, List<Vector3> pos, List<Vector3> nrm,
                                      List<Vector4> tan, List<Color> col, List<Vector2>[] uvs)
        {
            var output = new List<int>();
            for (int i = 0; i < poly.Count; i++)
            {
                int a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = keepBelow ? at - pos[a][axis] : pos[a][axis] - at;
                float db = keepBelow ? at - pos[b][axis] : pos[b][axis] - at;
                if (da >= 0f) output.Add(a);
                if ((da >= 0f) != (db >= 0f))
                {
                    float t = da / (da - db);
                    int count = pos.Count;
                    pos.Add(Vector3.Lerp(pos[a], pos[b], t));
                    if (nrm.Count == count) nrm.Add(Vector3.Lerp(nrm[a], nrm[b], t).normalized);
                    if (tan.Count == count) tan.Add(Vector4.Lerp(tan[a], tan[b], t));
                    if (col.Count == count) col.Add(Color.Lerp(col[a], col[b], t));
                    foreach (List<Vector2> uv in uvs) if (uv.Count == count) uv.Add(Vector2.Lerp(uv[a], uv[b], t));
                    output.Add(count);
                }
            }
            return output;
        }
    }
}
