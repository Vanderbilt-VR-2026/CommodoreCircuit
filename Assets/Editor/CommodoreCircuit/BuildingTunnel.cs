using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Cuts a drive-through overhang (a tunnel open at both ends) into a building mesh
    /// along a stretch of the track: everything below Tunnel Height inside the track
    /// corridor is removed, and tunnel walls and a ceiling are added so it looks solid.
    ///
    /// Always cuts from the building's ORIGINAL mesh asset, so re-running is safe.
    /// The original mesh asset is never changed; the result is saved as a new asset.
    /// </summary>
    public static class BuildingTunnel
    {
        private const string OriginalMeshDir = "Assets/CampusScenery/Meshes/Buildings/";

        private struct V { public Vector3 p; public Vector2 uv; }
        private struct Pl { public Vector3 n; public float d; public float Dist(Vector3 p) => Vector3.Dot(n, p) - d; }

        public static bool Cut(string buildingName, Vector3[] center, Vector3[] left, float halfWidth, float height, string saveDir)
        {
            GameObject go = GameObject.Find(buildingName);
            if (go == null) { Debug.Log($"Tunnel: '{buildingName}' not in this scene, skipped."); return false; }
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null) return false;

            Mesh src = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMeshDir + buildingName + ".asset");
            if (src == null) src = mf.sharedMesh;
            if (src == null) return false;

            Transform t = go.transform;
            Bounds b = src.bounds;

            // Track corridor pieces (one convex quad per centerline segment) in the building's local space.
            List<Vector3[]> quads = new List<Vector3[]>();
            int n = center.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 dir = (center[j] - center[i]).normalized * 0.1f; // tiny overlap, no slivers
                Vector3[] q =
                {
                    t.InverseTransformPoint(center[i] - dir + left[i] * halfWidth),
                    t.InverseTransformPoint(center[j] + dir + left[j] * halfWidth),
                    t.InverseTransformPoint(center[j] + dir - left[j] * halfWidth),
                    t.InverseTransformPoint(center[i] - dir - left[i] * halfWidth),
                };
                float minX = Mathf.Min(q[0].x, q[1].x, q[2].x, q[3].x), maxX = Mathf.Max(q[0].x, q[1].x, q[2].x, q[3].x);
                float minZ = Mathf.Min(q[0].z, q[1].z, q[2].z, q[3].z), maxZ = Mathf.Max(q[0].z, q[1].z, q[2].z, q[3].z);
                if (maxX < b.min.x || minX > b.max.x || maxZ < b.min.z || minZ > b.max.z) continue;
                quads.Add(q);
            }
            if (quads.Count == 0) { Debug.Log($"Tunnel: track does not cross '{buildingName}'."); return false; }

            // Every triangle of the building, per submesh, as polygons.
            Vector3[] verts = src.vertices;
            Vector2[] uvs = src.uv.Length == verts.Length ? src.uv : new Vector2[verts.Length];
            int subCount = src.subMeshCount;
            List<List<V>>[] polys = new List<List<V>>[subCount];
            for (int s = 0; s < subCount; s++)
            {
                polys[s] = new List<List<V>>();
                int[] tris = src.GetTriangles(s);
                for (int k = 0; k < tris.Length; k += 3)
                    polys[s].Add(new List<V>
                    {
                        new V { p = verts[tris[k]], uv = uvs[tris[k]] },
                        new V { p = verts[tris[k + 1]], uv = uvs[tris[k + 1]] },
                        new V { p = verts[tris[k + 2]], uv = uvs[tris[k + 2]] },
                    });
            }

            // Remove the corridor volume below the tunnel height.
            foreach (Vector3[] q in quads)
            {
                Pl[] prism = PrismPlanes(q, height);
                for (int s = 0; s < subCount; s++)
                {
                    List<List<V>> kept = new List<List<V>>();
                    foreach (List<V> poly in polys[s]) SubtractConvex(poly, prism, kept);
                    polys[s] = kept;
                }
            }

            // Tunnel ceiling and side walls, limited to the building footprint.
            List<Vector2> hull = FootprintHull(verts);
            int wallSub = 0;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                for (int s = 0; s < mr.sharedMaterials.Length && s < subCount; s++)
                    if (mr.sharedMaterials[s] != null && mr.sharedMaterials[s].name.Contains("Brick")) { wallSub = s; break; }

            foreach (Vector3[] q in quads)
            {
                // Ceiling (faces down).
                List<Vector2> ceil = ClipConvex(new List<Vector2> { XZ(q[0]), XZ(q[1]), XZ(q[2]), XZ(q[3]) }, hull);
                for (int k = 1; k + 1 < ceil.Count; k++)
                    AddTri(polys[wallSub], P(ceil[0], height), P(ceil[k], height), P(ceil[k + 1], height), Vector3.down);

                // Side walls (face into the tunnel).
                Vector3 mid = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                AddWall(polys[wallSub], q[0], q[1], hull, height, mid);
                AddWall(polys[wallSub], q[3], q[2], hull, height, mid);
            }

            // Rebuild the mesh (flat shaded, like the scenery).
            List<Vector3> outV = new List<Vector3>();
            List<Vector2> outUV = new List<Vector2>();
            List<int>[] outT = new List<int>[subCount];
            for (int s = 0; s < subCount; s++)
            {
                outT[s] = new List<int>();
                foreach (List<V> poly in polys[s])
                    for (int k = 1; k + 1 < poly.Count; k++)
                    {
                        V a = poly[0], c1 = poly[k], c2 = poly[k + 1];
                        if (Vector3.Cross(c1.p - a.p, c2.p - a.p).sqrMagnitude < 1e-8f) continue;
                        outT[s].Add(outV.Count); outV.Add(a.p); outUV.Add(a.uv);
                        outT[s].Add(outV.Count); outV.Add(c1.p); outUV.Add(c1.uv);
                        outT[s].Add(outV.Count); outV.Add(c2.p); outUV.Add(c2.uv);
                    }
            }

            Mesh m = new Mesh { name = buildingName + " (Tunnel)", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(outV);
            m.SetUVs(0, outUV);
            m.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) m.SetTriangles(outT[s], s);
            m.RecalculateNormals();
            m.RecalculateBounds();

            string path = saveDir + "/Carmichael_Tunnel.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);

            Undo.RecordObject(mf, "Cut tunnel");
            mf.sharedMesh = m;
            MeshCollider mc = go.GetComponent<MeshCollider>();
            if (mc != null) { Undo.RecordObject(mc, "Cut tunnel"); mc.sharedMesh = m; }
            EditorUtility.SetDirty(go);
            return true;
        }

        // Planes of the corridor prism; normals point OUT of the volume.
        private static Pl[] PrismPlanes(Vector3[] q, float height)
        {
            Vector3 c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            List<Pl> planes = new List<Pl>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = q[i], b = q[(i + 1) % 4];
                Vector3 edge = b - a; edge.y = 0f;
                Vector3 nrm = new Vector3(edge.z, 0f, -edge.x).normalized;
                Vector3 mid = (a + b) * 0.5f; mid.y = 0f;
                Vector3 cc = c; cc.y = 0f;
                if (Vector3.Dot(nrm, cc - mid) > 0f) nrm = -nrm;
                planes.Add(new Pl { n = nrm, d = Vector3.Dot(nrm, mid) });
            }
            planes.Add(new Pl { n = Vector3.up, d = height });
            planes.Add(new Pl { n = Vector3.down, d = 5f }); // floor far below ground
            return planes.ToArray();
        }

        // Adds the parts of poly OUTSIDE the convex volume to output.
        private static void SubtractConvex(List<V> poly, Pl[] planes, List<List<V>> output)
        {
            List<V> inside = poly;
            foreach (Pl pl in planes)
            {
                Split(inside, pl, out List<V> outPart, out List<V> inPart);
                if (outPart.Count >= 3) output.Add(outPart);
                inside = inPart;
                if (inside.Count < 3) return;
            }
            // whatever is left is inside the tunnel: dropped
        }

        private static void Split(List<V> poly, Pl pl, out List<V> outside, out List<V> inside)
        {
            outside = new List<V>(); inside = new List<V>();
            const float eps = 1e-4f;
            for (int i = 0; i < poly.Count; i++)
            {
                V a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = pl.Dist(a.p), db = pl.Dist(b.p);
                if (da > eps) outside.Add(a); else if (da < -eps) inside.Add(a); else { outside.Add(a); inside.Add(a); }
                if ((da > eps && db < -eps) || (da < -eps && db > eps))
                {
                    float t = da / (da - db);
                    V m = new V { p = Vector3.Lerp(a.p, b.p, t), uv = Vector2.Lerp(a.uv, b.uv, t) };
                    outside.Add(m); inside.Add(m);
                }
            }
        }

        private static void AddWall(List<List<V>> into, Vector3 a, Vector3 b, List<Vector2> hull, float height, Vector3 tunnelMid)
        {
            if (!ClipSegment(XZ(a), XZ(b), hull, out Vector2 s0, out Vector2 s1)) return;
            Vector3 p0 = P(s0, 0f), p1 = P(s1, 0f), p2 = P(s1, height), p3 = P(s0, height);
            Vector3 inward = tunnelMid - (p0 + p1) * 0.5f; inward.y = 0f;
            AddTri(into, p0, p1, p2, inward);
            AddTri(into, p0, p2, p3, inward);
        }

        private static void AddTri(List<List<V>> into, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            // Unity's RecalculateNormals uses cross(b-a, c-a).
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            into.Add(new List<V>
            {
                new V { p = a, uv = new Vector2(a.x + a.z, a.y) * 0.25f },
                new V { p = b, uv = new Vector2(b.x + b.z, b.y) * 0.25f },
                new V { p = c, uv = new Vector2(c.x + c.z, c.y) * 0.25f },
            });
        }

        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        private static Vector3 P(Vector2 v, float y) => new Vector3(v.x, y, v.y);

        // Convex hull (counter-clockwise) of the building's footprint.
        private static List<Vector2> FootprintHull(Vector3[] verts)
        {
            List<Vector2> pts = new List<Vector2>();
            foreach (Vector3 v in verts) pts.Add(XZ(v));
            pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            List<Vector2> h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(p);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // Sutherland-Hodgman: subject clipped to the convex CCW clip polygon.
        private static List<Vector2> ClipConvex(List<Vector2> subject, List<Vector2> clip)
        {
            List<Vector2> output = subject;
            for (int i = 0; i < clip.Count && output.Count > 0; i++)
            {
                Vector2 a = clip[i], b = clip[(i + 1) % clip.Count];
                List<Vector2> input = output;
                output = new List<Vector2>();
                for (int k = 0; k < input.Count; k++)
                {
                    Vector2 p = input[k], q = input[(k + 1) % input.Count];
                    bool pin = Cross(a, b, p) >= 0f, qin = Cross(a, b, q) >= 0f;
                    if (pin) output.Add(p);
                    if (pin != qin)
                    {
                        float dp = Cross(a, b, p), dq = Cross(a, b, q);
                        output.Add(Vector2.Lerp(p, q, dp / (dp - dq)));
                    }
                }
            }
            return output;
        }

        // Clips segment a-b to the convex CCW polygon.
        private static bool ClipSegment(Vector2 a, Vector2 b, List<Vector2> clip, out Vector2 s0, out Vector2 s1)
        {
            float t0 = 0f, t1 = 1f;
            for (int i = 0; i < clip.Count; i++)
            {
                Vector2 e0 = clip[i], e1 = clip[(i + 1) % clip.Count];
                float da = Cross(e0, e1, a), db = Cross(e0, e1, b);
                if (da < 0f && db < 0f) { s0 = s1 = Vector2.zero; return false; }
                if (da < 0f) t0 = Mathf.Max(t0, da / (da - db));
                else if (db < 0f) t1 = Mathf.Min(t1, da / (da - db));
            }
            s0 = Vector2.Lerp(a, b, t0); s1 = Vector2.Lerp(a, b, t1);
            return t1 - t0 > 1e-4f;
        }
    }
}
