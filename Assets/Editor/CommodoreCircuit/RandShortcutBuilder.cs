using System;
using System.Collections.Generic;
using System.IO;
using CommodoreCircuit.Race;
using CommodoreCircuit.Scooter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Build Rand Shortcut
    ///
    /// Builds the shortcut through Sarratt and Rand Hall in the OPEN scene (Alumni Lawn Scenery)
    /// from Assets/CampusTrack/rand_shortcut.json:
    ///   - a floor you drive on, in the run-off colour, with a faint worn strip down the middle
    ///     as a hint that this is a way through,
    ///   - invisible walls down both sides (the buildings have no colliders of their own),
    ///   - openings in the lap's left barrier where the shortcut leaves and rejoins,
    ///   - a passage cut through each building (BuildingTunnel), blank inside for now,
    ///   - a TrackShortcut, so laps, positions and the wrong-way warning work through it.
    ///
    /// Run it after Build Alumni Lawn Track: that rebuilds the barriers without the openings.
    /// Running this again replaces the "Rand Shortcut" object. Undo works for the scene changes.
    /// </summary>
    public static class RandShortcutBuilder
    {
        private const string DataPath = "Assets/CampusTrack/rand_shortcut.json";
        private const string TrackDataPath = "Assets/CampusTrack/alumni_track_route.json";
        private const string MeshDir = "Assets/CampusTrack/ShortcutMeshes";
        private const string MatDir = "Assets/CampusTrack/Materials";
        private const string RootName = "Rand Shortcut";
        private const string MainBarrierName = "Barrier Left";

        // Between the lap's run-off (0.08) and road (0.11): it shows over the run-off and
        // tucks under the road where the two overlap at each end.
        private const float FloorY = 0.095f;
        private const float WornY = 0.1f;

        [Serializable] private class Meta { public float floorHalfWidth, wallOffset, tunnelHalfWidth, tunnelHeight, wornHalfWidth; }
        [Serializable] private class Data { public Meta meta; public string[] buildings; public float[] centerline; }
        [Serializable] private class TrackData { public float[] centerline, barrierLeft, barrierRight; }

        [MenuItem("CommodoreCircuit/Build Rand Shortcut")]
        private static void Build()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            var trackJson = AssetDatabase.LoadAssetAtPath<TextAsset>(TrackDataPath);
            if (json == null || trackJson == null)
            {
                EditorUtility.DisplayDialog("Route missing", $"Could not find {DataPath} or {TrackDataPath}.", "OK");
                return;
            }
            Data data = JsonUtility.FromJson<Data>(json.text);
            TrackData lap = JsonUtility.FromJson<TrackData>(trackJson.text);

            Vector3[] center = ToPoints(data.centerline);
            Vector3[] left = LeftVectors(center, false);
            int n = center.Length;
            Meta m = data.meta;

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder(MeshDir);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Rand Shortcut");

            // Floor in the run-off colour, so it reads as a continuation of the track's edge
            // rather than a second road. The worn strip is the hint.
            Material floor = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/RunOff.mat")
                ?? Mat("RunOff", new Color(0.62f, 0.60f, 0.56f));
            Material worn = Mat("ShortcutWorn", new Color(0.53f, 0.515f, 0.48f));
            Part(root, "Floor", Ribbon("ShortcutFloor", center, left, Fill(n, m.floorHalfWidth), FloorY), floor, true);
            Part(root, "Worn Strip", Ribbon("ShortcutWorn", center, left, WornWidths(center, m.wornHalfWidth), WornY), worn, false);

            // Side walls, only where the shortcut is outside the lap's barriers.
            Vector3[] lapCenter = ToPoints(lap.centerline);
            Vector3[] lapLeft = LeftVectors(lapCenter, true);
            GameObject walls = new GameObject("Walls");
            walls.transform.SetParent(root.transform, false);
            foreach (float side in new[] { 1f, -1f })
            {
                var line = new List<Vector3>();
                for (int i = 0; i < n; i++) line.Add(center[i] + left[i] * m.wallOffset * side);
                foreach (List<Vector3> run in Runs(line, p => !InsideLapBarriers(p, lapCenter, lapLeft, lap), false))
                    MakeWall(walls, side > 0 ? "Wall Left" : "Wall Right", run);
            }

            // Openings in the lap's left barrier (the shortcut leaves and rejoins on that side).
            int openings = OpenLapBarrier(center, m.wallOffset);

            // Passages through the buildings.
            var cut = new List<string>();
            foreach (string building in data.buildings)
                if (BuildingTunnel.Cut(building, center, left, m.tunnelHalfWidth, m.tunnelHeight, MeshDir, closed: false))
                    cut.Add(building);

            // Lap counting through the shortcut.
            root.AddComponent<TrackShortcut>().SetPath(center, m.floorHalfWidth);

            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            string msg = $"Shortcut built: {PathLength(center):0} m long.\n" +
                         $"Passage cut through: {(cut.Count > 0 ? string.Join(", ", cut) : "nothing (buildings not found)")}.\n" +
                         $"Opened {openings} gap(s) in {MainBarrierName}.\n\nSave the scene (Cmd+S).";
            Debug.Log("Rand shortcut: " + msg);
            EditorUtility.DisplayDialog("Rand shortcut built", msg, "OK");
        }

        // ---- Lap barrier openings ----

        // Removes the parts of every "Barrier Left" path that fall between the shortcut's walls
        // and splits what's left into separate open paths. Safe to run again: once a gap is
        // open there's nothing left inside it to remove.
        private static int OpenLapBarrier(Vector3[] shortcut, float wallOffset)
        {
            var paths = new List<BarrierPath>();
            foreach (BarrierPath bp in UnityEngine.Object.FindObjectsByType<BarrierPath>(FindObjectsSortMode.None))
                if (bp.name.StartsWith(MainBarrierName)) paths.Add(bp);

            int gaps = 0;
            foreach (BarrierPath bp in paths)
            {
                var world = new List<Vector3>();
                for (int i = 0; i < bp.points.Count; i++) world.Add(bp.WorldPoint(i));
                List<Vector3> dense = Densify(world, bp.closedLoop, 1f);
                bool InGap(Vector3 p) => DistanceToPath(p, shortcut) < wallOffset;
                if (!dense.Exists(InGap)) continue;

                List<List<Vector3>> runs = Runs(dense, p => !InGap(p), bp.closedLoop);
                gaps += bp.closedLoop ? runs.Count : runs.Count - 1;

                for (int r = 0; r < runs.Count; r++)
                {
                    BarrierPath target = bp;
                    if (r > 0)
                    {
                        var go = new GameObject($"{MainBarrierName} ({paths.Count + r})");
                        Undo.RegisterCreatedObjectUndo(go, "Open lap barrier");
                        go.transform.SetParent(bp.transform.parent, false);
                        go.transform.SetPositionAndRotation(bp.transform.position, bp.transform.rotation);
                        go.transform.localScale = bp.transform.localScale;
                        target = go.AddComponent<BarrierPath>();
                        EditorUtility.CopySerialized(bp, target);
                        go.AddComponent<TrackWall>();
                    }
                    else
                    {
                        Undo.RecordObject(bp, "Open lap barrier");
                    }
                    target.closedLoop = false;
                    target.points.Clear();
                    foreach (Vector3 p in Simplify(runs[r])) target.points.Add(target.transform.InverseTransformPoint(p));
                    EditorUtility.SetDirty(target);
                    BarrierPathEditor.Build(target);
                }
            }
            return gaps;
        }

        // ---- Walls ----

        private static void MakeWall(GameObject parent, string name, List<Vector3> points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var path = go.AddComponent<BarrierPath>();
            go.AddComponent<TrackWall>();
            path.closedLoop = false;
            // Inside the buildings the tunnel walls are what you see; outside it's open ground
            // between the buildings, where a row of barriers would give the shortcut away.
            path.showVisibleBarrier = false;
            path.points.Clear();
            foreach (Vector3 p in Simplify(points)) path.points.Add(p);
            BarrierPathEditor.Build(path);
        }

        private static bool InsideLapBarriers(Vector3 p, Vector3[] lapCenter, Vector3[] lapLeft, TrackData lap)
        {
            int n = lapCenter.Length;
            int best = 0;
            float bestDist = float.MaxValue, bestT = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = lapCenter[i], ab = lapCenter[(i + 1) % n] - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; bestT = t; }
            }
            int j = (best + 1) % n;
            Vector3 closest = Vector3.Lerp(lapCenter[best], lapCenter[j], bestT);
            float lateral = Vector3.Dot(p - closest, Vector3.Lerp(lapLeft[best], lapLeft[j], bestT).normalized);
            float bL = Mathf.Lerp(lap.barrierLeft[best], lap.barrierLeft[j], bestT);
            float bR = Mathf.Lerp(lap.barrierRight[best], lap.barrierRight[j], bestT);
            return lateral < bL && lateral > -bR;
        }

        // ---- Geometry helpers ----

        // Splits a line into the stretches where keep() holds. For a closed line the stretch
        // that wraps past the first point is joined up, so it isn't cut in two.
        private static List<List<Vector3>> Runs(List<Vector3> line, Func<Vector3, bool> keep, bool closed)
        {
            var runs = new List<List<Vector3>>();
            int n = line.Count;
            int start = 0;
            if (closed)
            {
                start = -1;
                for (int i = 0; i < n; i++) if (!keep(line[i])) { start = i; break; }
                if (start < 0) { runs.Add(new List<Vector3>(line)); return runs; }
            }
            List<Vector3> current = null;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = line[(start + k) % n];
                if (keep(p))
                {
                    if (current == null) { current = new List<Vector3>(); runs.Add(current); }
                    current.Add(p);
                }
                else current = null;
            }
            runs.RemoveAll(r => r.Count < 2);
            return runs;
        }

        private static List<Vector3> Densify(List<Vector3> pts, bool closed, float step)
        {
            var dense = new List<Vector3>();
            int segs = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                for (int k = 0; k < count; k++) dense.Add(Vector3.Lerp(a, b, k / (float)count));
            }
            if (!closed && pts.Count > 0) dense.Add(pts[pts.Count - 1]);
            return dense;
        }

        // Keep a point only where the line bends (or every 15 m), like the lap's barriers.
        private static List<Vector3> Simplify(List<Vector3> pts)
        {
            var kept = new List<Vector3> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 last = kept[kept.Count - 1];
                Vector3 dirIn = (pts[i] - last).normalized, dirOut = (pts[i + 1] - pts[i]).normalized;
                if (Vector3.Angle(dirIn, dirOut) > 4f || Vector3.Distance(pts[i], last) > 15f) kept.Add(pts[i]);
            }
            kept.Add(pts[pts.Count - 1]);
            return kept;
        }

        private static float DistanceToPath(Vector3 p, Vector3[] path)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Vector3 a = path[i], ab = path[i + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                Vector3 d = a + ab * t - p;
                d.y = 0f;
                best = Mathf.Min(best, d.magnitude);
            }
            return best;
        }

        private static Vector3[] ToPoints(float[] xz)
        {
            var pts = new Vector3[xz.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(xz[i * 2], 0f, xz[i * 2 + 1]);
            return pts;
        }

        // Unit vectors to the left of travel at each point.
        private static Vector3[] LeftVectors(Vector3[] c, bool closed)
        {
            int n = c.Length;
            var left = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = closed ? c[(i - 1 + n) % n] : c[Mathf.Max(0, i - 1)];
                Vector3 b = closed ? c[(i + 1) % n] : c[Mathf.Min(n - 1, i + 1)];
                Vector3 t = b - a;
                t.y = 0f;
                t.Normalize();
                left[i] = new Vector3(-t.z, 0f, t.x);
            }
            return left;
        }

        private static float[] Fill(int n, float value)
        {
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = value;
            return w;
        }

        // The worn strip fades in and out over the first and last 8 m, so it doesn't start
        // as a hard-edged stripe on the lap.
        private static float[] WornWidths(Vector3[] c, float half)
        {
            int n = c.Length;
            var w = new float[n];
            float total = PathLength(c), along = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Vector3.Distance(c[i - 1], c[i]);
                w[i] = half * Mathf.Clamp01(Mathf.Min(along, total - along) / 8f);
            }
            return w;
        }

        private static float PathLength(Vector3[] c)
        {
            float d = 0f;
            for (int i = 1; i < c.Length; i++) d += Vector3.Distance(c[i - 1], c[i]);
            return d;
        }

        // A flat open strip along the centerline, faces up.
        private static Mesh Ribbon(string name, Vector3[] center, Vector3[] left, float[] half, float y)
        {
            int n = center.Length;
            var v = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) dist += Vector3.Distance(center[i], center[i - 1]);
                Vector3 c = new Vector3(center[i].x, y, center[i].z);
                v[i * 2] = c + left[i] * half[i];
                v[i * 2 + 1] = c - left[i] * half[i];
                uv[i * 2] = new Vector2(0f, dist / 4f);
                uv[i * 2 + 1] = new Vector2(1f, dist / 4f);
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = (i + 1) * 2, k = i * 6;
                // Left, then right of travel: a, b, a+1 winds clockwise seen from above (faces up).
                tris[k] = a; tris[k + 1] = b; tris[k + 2] = a + 1;
                tris[k + 3] = a + 1; tris[k + 4] = b; tris[k + 5] = b + 1;
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                Array.Reverse(tris);
                mesh.triangles = tris;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            LightmapUVs.Generate(mesh);
            AssetDatabase.CreateAsset(mesh, MeshDir + "/" + name + ".asset");
            return mesh;
        }

        private static void Part(GameObject parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        private static Material Mat(string name, Color color)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;   // reuse existing materials untouched
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
