using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Getting the campus ready for baked lighting.
    ///
    /// CommodoreCircuit > Lighting > Generate Lightmap UVs
    ///   Baked lighting needs a second UV set (UV2) on every mesh, so each surface gets its own
    ///   spot in the lightmap. Imported models get it from "Generate Lightmap UVs" on import;
    ///   the campus meshes are built by our own builders and saved as .asset files, so they
    ///   have none. This adds it to every mesh under Assets/CampusScenery and Assets/CampusTrack.
    ///   The builders also call <see cref="Generate"/> as they save, so a rebuild keeps it.
    ///
    /// CommodoreCircuit > Lighting > Mark Campus Static
    ///   Marks everything in the open scene that uses one of those meshes as static, so the
    ///   bake includes it. Small things (props under Small Object Size) are lit by light probes
    ///   instead of taking up lightmap space.
    ///
    /// After both: set the sun to Mixed, then Window > Rendering > Lighting > Generate Lighting.
    /// </summary>
    public static class LightmapUVs
    {
        private static readonly string[] Folders = { "Assets/CampusScenery", "Assets/CampusTrack" };

        // Gap between UV islands, as a fraction of the UV square. Unity's default (1/256) bleeds at
        // the low texel density a whole campus has to use on Quest; 1/64 keeps islands apart.
        private const float PackMargin = 1f / 64f;

        // Anything smaller than this (largest side, metres) gets light probes instead of a lightmap.
        private const float SmallObjectSize = 2.5f;

        /// <summary>Adds lightmap UVs (UV2) to a mesh. Builders call this just before saving a mesh.</summary>
        public static void Generate(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            UnwrapParam.SetDefaults(out UnwrapParam settings);
            settings.packMargin = PackMargin;
            Unwrapping.GenerateSecondaryUVSet(mesh, settings);
        }

        [MenuItem("CommodoreCircuit/Lighting/Generate Lightmap UVs")]
        private static void GenerateAll()
        {
            var meshes = new List<Mesh>();
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", Folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".asset")) continue;   // imported models use their own import setting
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh != null && mesh.uv2.Length == 0) meshes.Add(mesh);
            }

            if (meshes.Count == 0)
            {
                Debug.Log("[Lighting] Every campus mesh already has lightmap UVs.");
                return;
            }

            int done = 0;
            try
            {
                for (; done < meshes.Count; done++)
                {
                    Mesh mesh = meshes[done];
                    if (EditorUtility.DisplayCancelableProgressBar("Generating lightmap UVs",
                            $"{mesh.name} ({done + 1}/{meshes.Count})", done / (float)meshes.Count))
                        break;
                    Generate(mesh);
                    EditorUtility.SetDirty(mesh);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Lighting] Added lightmap UVs to {done} of {meshes.Count} campus meshes."
                      + (done < meshes.Count ? " Cancelled; run it again to finish the rest." : ""));
        }

        [MenuItem("CommodoreCircuit/Lighting/Mark Campus Static")]
        private static void MarkStatic()
        {
            const StaticEditorFlags flags = StaticEditorFlags.ContributeGI
                                            | StaticEditorFlags.ReflectionProbeStatic
                                            | StaticEditorFlags.BatchingStatic;
            int marked = 0, probeLit = 0;

            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!IsCampusMesh(filter.sharedMesh)) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                GameObject go = filter.gameObject;
                Undo.RecordObject(go, "Mark Campus Static");
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | flags);

                Undo.RecordObject(renderer, "Mark Campus Static");
                Vector3 size = renderer.bounds.size;
                bool small = Mathf.Max(size.x, size.y, size.z) < SmallObjectSize;
                renderer.receiveGI = small ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                if (small) probeLit++;
                marked++;
            }

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Lighting] Marked {marked} campus objects static ({probeLit} small ones lit by light probes). "
                      + "Save the scene to keep it.");
        }

        private static bool IsCampusMesh(Mesh mesh)
        {
            if (mesh == null) return false;
            string path = AssetDatabase.GetAssetPath(mesh);
            foreach (string folder in Folders)
                if (path.StartsWith(folder + "/")) return true;
            return false;
        }
    }
}
