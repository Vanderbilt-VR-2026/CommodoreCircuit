using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Environment > Swap In Campus Models
    ///
    /// Replaces gray-box scenery in the open scene (Alumni Lawn Scenery) with Tifa's Blender models
    /// from Assets/CommodoreCircuit/Environment/Models:
    ///   - buildings listed in Buildings (below): the model is lined up with the gray box's footprint
    ///     (center + long side) and set on the ground; the gray box is switched OFF, not deleted,
    ///   - every lamp post, and every tree (three tree shapes, mixed),
    ///   - trees and lamp posts standing inside the race track are hidden.
    ///
    /// First it makes clean prefabs in Environment/Prefabs, keeping only the model itself (Blender's
    /// default Cube, Camera and Light in the FBX files are dropped). Safe to run again: it replaces
    /// what it made last time. To add a building: put its FBX in Models and add a line to Buildings.
    /// If a building faces the wrong way, rotate its "(Model)" object 180 degrees on Y.
    /// </summary>
    public static class CampusModelSwapper
    {
        private const string ModelDir = "Assets/CommodoreCircuit/Environment/Models";
        private const string PrefabDir = "Assets/CommodoreCircuit/Environment/Prefabs";
        private const string RoutePath = "Assets/CampusTrack/alumni_track_route.json";
        private const string ModelSuffix = " (Model)";

        // gray box name in the scene -> (FBX file, object inside the FBX)
        private static readonly (string grayBox, string fbx, string part)[] Buildings =
        {
            ("Alumni Hall", "AlumniHallExterior.fbx", "AlumniHallExterior"),
            // ("McGill Hall", "McGillHallExterior.fbx", "McGillHallExterior"),   // on hold: resizing in Blender
        };

        // Buildings that must sit at an exact spot (their gates line up with the race track).
        // pivot = world X/Z of the model's base centre; frameYaw = Unity yaw that turns the
        // Blender model's +X axis onto the track frame. The tool tries the four 90-degree turns
        // from there and keeps the one where the track runs clear through the building.
        private static readonly (string grayBox, string fbx, string part, Vector2 pivot, float frameYaw)[] ExactBuildings =
        {
            // track frame: centre (-155.74, 4.39), axis at 29.5 deg; model base centre is (+2.75, +1.5) in that frame
            ("Oliver C. Carmichael College", "CarmichaelCollegeExterior.fbx", "Carmichael_College",
             TrackFramePoint(-155.74f, 4.39f, 29.5f, 2.75f, 1.5f), -29.5f),
        };

        private static Vector2 TrackFramePoint(float cx, float cz, float deg, float a, float b)
        {
            float r = deg * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(r), Mathf.Sin(r)), w = new Vector2(-u.y, u.x);
            return new Vector2(cx, cz) + u * a + w * b;
        }

        // Prefabs made but not placed yet.
        private static readonly (string fbx, string part)[] Extras =
        {
            ("McGillHallExterior.fbx", "McGillHallExterior"),
            ("WestEndTower.fbx", "WestEndTower"),
        };

        private const string LampFbx = "Campus_Lamppost.fbx";
        private const string LampPart = "CampusLamppost";
        private const string TreeFbx = "CampusTrees.fbx";
        private static readonly string[] TreeParts = { "CampusTree_Broad", "CampusTree_Small", "CampusTree_Tall" };

        [Serializable] private class RouteData { public float[] centerline, barrierLeft, barrierRight; }

        [MenuItem("CommodoreCircuit/Environment/Swap In Campus Models")]
        private static void SwapAll()
        {
            EnsureFolder("Assets/CommodoreCircuit");
            EnsureFolder("Assets/CommodoreCircuit/Environment");
            EnsureFolder(ModelDir);
            EnsureFolder(PrefabDir);

            var report = new List<string>();

            // Buildings
            foreach (var b in Buildings)
            {
                GameObject prefab = MakePrefab(b.fbx, b.part, report);
                if (prefab == null) continue;
                report.Add(SwapBuilding(b.grayBox, prefab));
            }
            foreach (var e in Extras) MakePrefab(e.fbx, e.part, report);

            // Props
            RouteData route = LoadRoute();
            foreach (var b in ExactBuildings)
            {
                GameObject prefab = MakePrefab(b.fbx, b.part, report);
                if (prefab == null) continue;
                report.Add(SwapExact(b.grayBox, prefab, b.pivot, b.frameYaw, route));
            }
            GameObject lamp = MakePrefab(LampFbx, LampPart, report);
            if (lamp != null) report.Add(SwapScatter("LampPosts", "LampPost_", new[] { lamp }, route, false));

            var trees = new List<GameObject>();
            foreach (string part in TreeParts)
            {
                GameObject t = MakePrefab(TreeFbx, part, report);
                if (t != null) trees.Add(t);
            }
            if (trees.Count > 0) report.Add(SwapScatter("Trees", "Tree_", trees.ToArray(), route, true));

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            string msg = string.Join("\n", report);
            Debug.Log("Swap In Campus Models:\n" + msg);
            EditorUtility.DisplayDialog("Campus models swapped in", msg + "\n\nSave the scene (Cmd+S).", "OK");
        }

        // ------------------------------------------------------------------ prefabs

        // A prefab holding only the named object from the FBX, with its pivot at the
        // center of its footprint, on the ground.
        private static GameObject MakePrefab(string fbxFile, string partName, List<string> report)
        {
            string fbxPath = ModelDir + "/" + fbxFile;
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) { report.Add($"Missing model: {fbxPath}"); return null; }

            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = Quaternion.identity;

            Transform part = null;
            // The FBX root often has the same name as the mesh inside it, so skip the root
            // and prefer the object that actually has a mesh.
            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
            {
                if (t == temp.transform || t.name != partName) continue;
                if (part == null || t.GetComponent<MeshFilter>() != null) part = t;
            }
            if (part == null && temp.name.StartsWith(Path.GetFileNameWithoutExtension(fbxFile)) && temp.GetComponent<MeshFilter>() != null)
            {
                // Single-mesh FBX: Unity makes the file's root the mesh itself.
                GameObject wrap = new GameObject(partName);
                temp.transform.SetParent(wrap.transform, true);
                temp.name = partName + "_Mesh";
                Bounds wb = WorldBounds(wrap);
                temp.transform.position -= new Vector3(wb.center.x, wb.min.y, wb.center.z);
                GameObject savedSingle = PrefabUtility.SaveAsPrefabAsset(wrap, PrefabDir + "/" + partName + ".prefab");
                UnityEngine.Object.DestroyImmediate(wrap);
                return savedSingle;
            }
            if (part == null)
            {
                UnityEngine.Object.DestroyImmediate(temp);
                report.Add($"'{partName}' not found inside {fbxFile}");
                return null;
            }

            GameObject root = new GameObject(partName);
            part.SetParent(root.transform, true);
            UnityEngine.Object.DestroyImmediate(temp);  // drops the Cube, Camera and Light

            // Remove leftovers that are not meshes (lights/cameras parented under the part).
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(l.gameObject);
            foreach (Camera c in root.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(c.gameObject);

            Bounds bounds = WorldBounds(root);
            part.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            string prefabPath = PrefabDir + "/" + partName + ".prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        // ------------------------------------------------------------------ buildings

        private static string SwapBuilding(string grayName, GameObject prefab)
        {
            GameObject gray = FindInScene(grayName);
            if (gray == null) return $"{grayName}: gray box not found in this scene.";

            // Gray box footprint: smallest rectangle around it, seen from above.
            var pts = new List<Vector2>();
            foreach (MeshFilter mf in gray.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(v);
                    pts.Add(new Vector2(w.x, w.z));
                }
            }
            if (pts.Count == 0) return $"{grayName}: gray box has no mesh.";
            MinRect(pts, out Vector2 center, out Vector2 longDir, out float longLen, out float shortLen);

            // Model footprint (prefab is axis aligned, pivot at footprint center).
            Bounds mb = PrefabBounds(prefab);
            bool modelLongIsX = mb.size.x >= mb.size.z;
            float yaw = modelLongIsX
                ? Mathf.Atan2(-longDir.y, longDir.x) * Mathf.Rad2Deg
                : Mathf.Atan2(longDir.x, longDir.y) * Mathf.Rad2Deg;

            // Replace an earlier swap.
            Transform parent = gray.transform.parent;
            Transform old = parent != null ? parent.Find(grayName + ModelSuffix) : null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, gray.scene);
            Undo.RegisterCreatedObjectUndo(inst, "Swap building");
            inst.name = grayName + ModelSuffix;
            inst.transform.SetParent(parent, false);
            inst.transform.SetPositionAndRotation(new Vector3(center.x, 0f, center.y), Quaternion.Euler(0f, yaw, 0f));
            GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);

            Undo.RecordObject(gray, "Hide gray box");
            gray.SetActive(false);

            float modelLong = Mathf.Max(mb.size.x, mb.size.z), modelShort = Mathf.Min(mb.size.x, mb.size.z);
            return $"{grayName}: swapped. Model {modelLong:0} x {modelShort:0} x {mb.size.y:0} m vs gray box {longLen:0} x {shortLen:0} m.";
        }

        // Places the model at a fixed pivot and picks the 90-degree turn where the track corridor
        // (between the barriers) has nothing of the building below 6 m.
        private static string SwapExact(string grayName, GameObject prefab, Vector2 pivot, float frameYaw, RouteData route)
        {
            GameObject gray = FindInScene(grayName);
            Scene scene = gray != null ? gray.scene : EditorSceneManager.GetActiveScene();
            Transform parent = gray != null ? gray.transform.parent : null;
            if (parent != null)
            {
                Transform old = parent.Find(grayName + ModelSuffix);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(inst, "Swap building");
            inst.name = grayName + ModelSuffix;
            if (parent != null) inst.transform.SetParent(parent, false);

            // temporary colliders for the clearance test
            var temp = new List<MeshCollider>();
            foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
            }
            var samples = CorridorSamples(route, pivot, 60f);
            float bestYaw = frameYaw; int best = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float yaw = frameYaw + 90f * k;
                inst.transform.SetPositionAndRotation(new Vector3(pivot.x, 0f, pivot.y), Quaternion.Euler(0f, yaw, 0f));
                Physics.SyncTransforms();
                int hits = 0;
                foreach (Vector2 s in samples)
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(s.x, 6f, s.y), Vector3.down, 5.8f, ~0, QueryTriggerInteraction.Ignore))
                        if (temp.Contains(h.collider as MeshCollider)) { hits++; break; }
                if (hits < best) { best = hits; bestYaw = yaw; }
            }
            foreach (MeshCollider mc in temp) UnityEngine.Object.DestroyImmediate(mc);
            inst.transform.SetPositionAndRotation(new Vector3(pivot.x, 0f, pivot.y), Quaternion.Euler(0f, bestYaw, 0f));
            GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);

            if (gray != null) { Undo.RecordObject(gray, "Hide gray box"); gray.SetActive(false); }
            return best == 0
                ? $"{grayName}: placed on the track (yaw {bestYaw:0.0}); the track runs clear through both gates."
                : $"{grayName}: placed (yaw {bestYaw:0.0}) but {best} of {samples.Count} track points touch the building. Check the gates.";
        }

        private static List<Vector2> CorridorSamples(RouteData r, Vector2 near, float radius)
        {
            var list = new List<Vector2>();
            if (r == null || r.centerline == null) return list;
            int n = r.centerline.Length / 2;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = new Vector2(r.centerline[i * 2], r.centerline[i * 2 + 1]);
                if ((c - near).sqrMagnitude > radius * radius) continue;
                int a = (i - 1 + n) % n, b = (i + 1) % n;
                Vector2 tan = (new Vector2(r.centerline[b * 2], r.centerline[b * 2 + 1]) - new Vector2(r.centerline[a * 2], r.centerline[a * 2 + 1])).normalized;
                Vector2 left = new Vector2(-tan.y, tan.x);
                float bl = r.barrierLeft != null && r.barrierLeft.Length == n ? r.barrierLeft[i] : 8.5f;
                float br = r.barrierRight != null && r.barrierRight.Length == n ? r.barrierRight[i] : 8.5f;
                for (int k = 0; k <= 8; k++)
                {
                    float t = Mathf.Lerp(-br - 0.3f, bl + 0.3f, k / 8f);
                    list.Add(c + left * t);
                }
            }
            return list;
        }

        private static void MinRect(List<Vector2> pts, out Vector2 center, out Vector2 longDir, out float longLen, out float shortLen)
        {
            float bestArea = float.MaxValue;
            center = Vector2.zero; longDir = Vector2.right; longLen = shortLen = 0f;
            for (float deg = 0f; deg < 180f; deg += 0.5f)
            {
                float a = deg * Mathf.Deg2Rad;
                Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), v = new Vector2(-u.y, u.x);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (Vector2 p in pts)
                {
                    float pu = Vector2.Dot(p, u), pv = Vector2.Dot(p, v);
                    minU = Mathf.Min(minU, pu); maxU = Mathf.Max(maxU, pu);
                    minV = Mathf.Min(minV, pv); maxV = Mathf.Max(maxV, pv);
                }
                float area = (maxU - minU) * (maxV - minV);
                if (area >= bestArea) continue;
                bestArea = area;
                center = u * (minU + maxU) * 0.5f + v * (minV + maxV) * 0.5f;
                bool uLong = maxU - minU >= maxV - minV;
                longDir = uLong ? u : v;
                longLen = Mathf.Max(maxU - minU, maxV - minV);
                shortLen = Mathf.Min(maxU - minU, maxV - minV);
            }
        }

        // ------------------------------------------------------------------ lamps & trees

        private static string SwapScatter(string groupName, string prefix, GameObject[] prefabs, RouteData route, bool isTree)
        {
            GameObject group = FindInScene(groupName);
            if (group == null) return $"{groupName}: group not found in this scene.";

            string newGroupName = groupName + ModelSuffix;
            Transform oldNew = group.transform.parent != null ? group.transform.parent.Find(newGroupName) : null;
            if (oldNew != null) Undo.DestroyObjectImmediate(oldNew.gameObject);

            GameObject newGroup = new GameObject(newGroupName);
            Undo.RegisterCreatedObjectUndo(newGroup, "Swap " + groupName);
            newGroup.transform.SetParent(group.transform.parent, false);

            int placed = 0, hidden = 0, i = 0;
            foreach (Transform t in group.transform)
            {
                if (!t.name.StartsWith(prefix)) continue;
                i++;
                Vector3 p = t.position;
                if (InsideTrack(route, p)) { hidden++; continue; }

                GameObject prefab = prefabs[isTree ? (Hash(t.name) % prefabs.Length) : 0];
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.scene);
                inst.name = t.name;
                inst.transform.SetParent(newGroup.transform, false);
                inst.transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.Euler(0f, t.eulerAngles.y, 0f));
                if (isTree)
                {
                    // Keep the size variety of the old trees (their scale was 0.8 to 1.44).
                    float s = Mathf.Clamp(t.localScale.x / 1.1f, 0.7f, 1.3f);
                    inst.transform.localScale = Vector3.one * s;
                }
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
                placed++;
            }

            Undo.RecordObject(group, "Hide " + groupName);
            group.SetActive(false);
            return $"{groupName}: {placed} placed, {hidden} left out because they stood on the track.";
        }

        private static int Hash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return Mathf.Abs(h);
            }
        }

        private static RouteData LoadRoute()
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
            return json != null ? JsonUtility.FromJson<RouteData>(json.text) : null;
        }

        // True if p is between the barriers (plus 1.5 m), so a tree there would stand in the race.
        private static bool InsideTrack(RouteData r, Vector3 p)
        {
            if (r == null || r.centerline == null) return false;
            int n = r.centerline.Length / 2;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float dx = p.x - r.centerline[i * 2], dz = p.z - r.centerline[i * 2 + 1];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (bestD > 40f * 40f) return false;
            Vector2 a = new Vector2(r.centerline[((best - 1 + n) % n) * 2], r.centerline[((best - 1 + n) % n) * 2 + 1]);
            Vector2 b = new Vector2(r.centerline[((best + 1) % n) * 2], r.centerline[((best + 1) % n) * 2 + 1]);
            Vector2 tan = (b - a).normalized, left = new Vector2(-tan.y, tan.x);
            Vector2 c = new Vector2(r.centerline[best * 2], r.centerline[best * 2 + 1]);
            float lateral = Vector2.Dot(new Vector2(p.x, p.z) - c, left);
            float bl = r.barrierLeft != null && r.barrierLeft.Length == n ? r.barrierLeft[best] : 8.5f;
            float br = r.barrierRight != null && r.barrierRight.Length == n ? r.barrierRight[best] : 8.5f;
            return lateral < bl + 1.5f && lateral > -br - 1.5f;
        }

        // ------------------------------------------------------------------ helpers

        private static GameObject FindInScene(string name)
        {
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            return null;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        private static Bounds PrefabBounds(GameObject prefab)
        {
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds b = WorldBounds(temp);
            UnityEngine.Object.DestroyImmediate(temp);
            return b;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>Import settings for the environment models: no cameras or lights from Blender.</summary>
    public class EnvironmentModelImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/CommodoreCircuit/Environment/Models")) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
        }
    }
}
