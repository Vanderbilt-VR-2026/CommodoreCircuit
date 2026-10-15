using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Build Lemon Scooter Model on Selected Scooter
    ///
    /// Replaces the placeholder box scooter with "Lemon", the game's yellow and
    /// white scooter: deck, rear fender, tail light, kickstand, and a steering
    /// assembly (fork, front fender, stem, headlight, handlebar, grips, display,
    /// lemon-slice emblem) that turns with the front wheel. Both wheels get a
    /// tire, a white rim, yellow spokes and a hub, and keep spinning from the
    /// existing controller code.
    ///
    /// Everything is built from Unity primitives plus two small generated
    /// meshes (the tire and the fenders), so it needs no outside art. Colors
    /// are Lemon yellow and white only, and the design is original. Safe to run
    /// again: it replaces the old model. The old placeholder body is switched
    /// off, not deleted.
    /// </summary>
    public static class LemonScooterBuilder
    {
        private const string ModelName = "LemonScooterModel";
        private const string MaterialFolder = "Assets/Materials/Lemon";
        private const string MeshFolder = "Assets/Models/Lemon";

        // Where a part's position is measured from. The steering assembly is built
        // around the front axle so it can turn about it.
        private static Vector3 origin = Vector3.zero;

        private static readonly Vector3 DefaultFrontAxle = new Vector3(0f, 0.15f, 0.6f);
        private static readonly Vector3 DefaultRearAxle = new Vector3(0f, 0.15f, -0.6f);

        [MenuItem("CommodoreCircuit/Build Lemon Scooter Model on Selected Scooter")]
        private static void Build()
        {
            GameObject scooterGO = Selection.activeGameObject;
            ScooterController controller = scooterGO != null ? scooterGO.GetComponent<ScooterController>() : null;
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Select the scooter",
                    "Select the scooter's root object in the Hierarchy (the one with the ScooterController component) and run this command again.",
                    "OK");
                return;
            }

            var so = new SerializedObject(controller);
            Vector3 frontAxle = AxlePosition(so, "frontWheel", DefaultFrontAxle);
            Vector3 rearAxle = AxlePosition(so, "rearWheel", DefaultRearAxle);
            Transform frontWheelVisual = so.FindProperty("frontWheelVisual").objectReferenceValue as Transform;
            Transform rearWheelVisual = so.FindProperty("rearWheelVisual").objectReferenceValue as Transform;

            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets/Materials", "Lemon");
            EnsureFolder("Assets", "Models");
            EnsureFolder("Assets/Models", "Lemon");

            Material yellow = Mat("LemonYellow", new Color(1f, 0.84f, 0.05f), 0.1f, 0.6f, null);
            Material white = Mat("LemonWhite", new Color(0.96f, 0.96f, 0.93f), 0.05f, 0.55f, null);
            Material black = Mat("LemonBlack", new Color(0.07f, 0.07f, 0.08f), 0f, 0.25f, null);
            Material tireMat = Mat("LemonTire", new Color(0.04f, 0.04f, 0.045f), 0f, 0.15f, null);
            Material lens = Mat("LemonHeadlight", new Color(1f, 0.97f, 0.82f), 0f, 0.8f, new Color(1f, 0.95f, 0.7f) * 2f);
            Material tail = Mat("LemonTailLight", new Color(0.9f, 0.1f, 0.08f), 0f, 0.6f, new Color(1f, 0.1f, 0.05f) * 1.5f);
            Material indicator = Mat("LemonIndicator", new Color(1f, 0.84f, 0.05f), 0f, 0.5f, new Color(1f, 0.8f, 0.1f) * 1.5f);

            Mesh tireMesh = SaveMesh(BuildTorus("LemonTire", 0.115f, 0.035f, 28, 12));
            Mesh rearFenderMesh = SaveMesh(BuildArcShell("LemonRearFender", 0.185f, 0.12f, 0.012f, 15f, 190f, 14));
            Mesh frontFenderMesh = SaveMesh(BuildArcShell("LemonFrontFender", 0.185f, 0.1f, 0.012f, 35f, 150f, 10));

            // Remove an earlier Lemon model, and switch off the placeholder body.
            RemoveChildren(scooterGO.transform, ModelName);
            foreach (Transform child in scooterGO.transform)
            {
                if (child.name.StartsWith("Body_Visual") && child.gameObject.activeSelf)
                {
                    Undo.RecordObject(child.gameObject, "Hide placeholder body");
                    child.gameObject.SetActive(false);
                }
            }

            var model = new GameObject(ModelName);
            Undo.RegisterCreatedObjectUndo(model, "Create Lemon Scooter");
            model.transform.SetParent(scooterGO.transform, false);

            // ---- Body: deck, rails, grip tape, rear fender, tail light, kickstand ----
            origin = Vector3.zero;
            Transform body = model.transform;
            Box("Deck", body, white, new Vector3(0f, 0.185f, -0.02f), new Vector3(0.2f, 0.04f, 0.88f));
            Box("GripTape", body, black, new Vector3(0f, 0.208f, -0.12f), new Vector3(0.17f, 0.006f, 0.62f));
            Box("RailLeft", body, yellow, new Vector3(-0.107f, 0.185f, -0.02f), new Vector3(0.014f, 0.03f, 0.88f));
            Box("RailRight", body, yellow, new Vector3(0.107f, 0.185f, -0.02f), new Vector3(0.014f, 0.03f, 0.88f));
            MeshPart("RearFender", body, rearFenderMesh, yellow, rearAxle, Vector3.zero);
            Box("TailLight", body, tail, new Vector3(0f, 0.275f, -0.752f), new Vector3(0.07f, 0.02f, 0.01f), new Vector3(-50f, 0f, 0f));
            Strut("Kickstand", body, black, new Vector3(0.07f, 0.17f, -0.2f), new Vector3(0.07f, 0.03f, -0.34f), 0.008f);

            // ---- Steering assembly, built around the front axle ----
            var front = new GameObject("FrontAssembly");
            Undo.RegisterCreatedObjectUndo(front, "Create Front Assembly");
            front.transform.SetParent(model.transform, false);
            front.transform.localPosition = frontAxle;
            Transform fa = front.transform;
            origin = frontAxle;

            Vector3 crown = new Vector3(0f, 0.6f, 0.545f);
            Vector3 stemTop = new Vector3(0f, 1.06f, 0.5f);
            Vector3 barCenter = new Vector3(0f, 1.08f, 0.5f);

            Strut("ForkLeft", fa, white, frontAxle + new Vector3(-0.052f, 0f, 0f), new Vector3(-0.052f, 0.6f, 0.545f), 0.014f);
            Strut("ForkRight", fa, white, frontAxle + new Vector3(0.052f, 0f, 0f), new Vector3(0.052f, 0.6f, 0.545f), 0.014f);
            Box("ForkCrown", fa, white, crown, new Vector3(0.13f, 0.03f, 0.05f));
            MeshPart("FrontFender", fa, frontFenderMesh, yellow, frontAxle, Vector3.zero);
            Strut("Stem", fa, yellow, crown, stemTop, 0.024f);

            // White cover on the lower stem, carrying the headlight and the lemon emblem.
            Vector3 coverCenter = new Vector3(0f, 0.74f, 0.532f);
            Box("StemCover", fa, white, coverCenter, new Vector3(0.09f, 0.26f, 0.085f), new Vector3(-3f, 0f, 0f));
            Sphere("Headlight", fa, lens, new Vector3(0f, 0.7f, 0.577f), new Vector3(0.055f, 0.055f, 0.022f));
            BuildEmblem(fa, yellow, white, coverCenter + new Vector3(-0.0455f, 0.02f, 0f), -1f);
            BuildEmblem(fa, yellow, white, coverCenter + new Vector3(0.0455f, 0.02f, 0f), 1f);

            // Handlebar, grips, brake levers and a small display.
            Cyl("Handlebar", fa, black, barCenter, 0.011f, 0.5f, new Vector3(0f, 0f, 90f));
            Cyl("GripLeft", fa, black, barCenter + new Vector3(-0.2f, 0f, 0f), 0.018f, 0.1f, new Vector3(0f, 0f, 90f));
            Cyl("GripRight", fa, black, barCenter + new Vector3(0.2f, 0f, 0f), 0.018f, 0.1f, new Vector3(0f, 0f, 90f));
            Box("BrakeLeft", fa, black, barCenter + new Vector3(-0.15f, -0.012f, 0.035f), new Vector3(0.012f, 0.012f, 0.075f));
            Box("BrakeRight", fa, black, barCenter + new Vector3(0.15f, -0.012f, 0.035f), new Vector3(0.012f, 0.012f, 0.075f));
            Box("Display", fa, white, barCenter + new Vector3(0f, 0.025f, -0.005f), new Vector3(0.1f, 0.04f, 0.06f));
            Box("DisplayLight", fa, indicator, barCenter + new Vector3(0f, 0.036f, 0.03f), new Vector3(0.05f, 0.008f, 0.004f));

            var barsAnchor = new GameObject("BarsAnchor");
            Undo.RegisterCreatedObjectUndo(barsAnchor, "Create Bars Anchor");
            barsAnchor.transform.SetParent(fa, false);
            barsAnchor.transform.localPosition = barCenter - frontAxle;

            // ---- Wheels: reuse the controller's wheel visuals so spin and steering still work ----
            origin = Vector3.zero;
            BuildWheel(frontWheelVisual, scooterGO.transform, frontAxle, tireMesh, tireMat, white, yellow);
            BuildWheel(rearWheelVisual, scooterGO.transform, rearAxle, tireMesh, tireMat, white, yellow);

            // ---- Hook the model into the controller ----
            so.Update();
            so.FindProperty("frontSteerAssembly").objectReferenceValue = front.transform;
            so.FindProperty("steeringBarsAnchor").objectReferenceValue = barsAnchor.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scooterGO.scene);
            Selection.activeGameObject = model;
            Debug.Log("Lemon scooter built under " + scooterGO.name + ". Save the scene. " +
                      "The old placeholder body is switched off (Body_Visual objects), not deleted. " +
                      "In play mode the fork, stem and handlebar turn with the front wheel. " +
                      "VR players grab the real handlebar.");
        }

        [MenuItem("CommodoreCircuit/Build Lemon Scooter Model on Selected Scooter", true)]
        private static bool ValidateBuild()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        // ------------------------------------------------------------------
        // Wheel: tire, white rim, five yellow spokes on each side, yellow hub.
        // Built in the wheel visual's own frame, where the wheel axis is local Y.
        // ------------------------------------------------------------------
        private static void BuildWheel(Transform wheelVisual, Transform scooterRoot, Vector3 axle, Mesh tireMesh, Material tireMat, Material white, Material yellow)
        {
            if (wheelVisual == null) return;

            // In play mode the controller moves each wheel onto its axle pivot. Put it on the
            // axle now as well so the wheels sit in the right place while editing.
            if (wheelVisual.parent == scooterRoot)
            {
                Undo.RecordObject(wheelVisual, "Place wheel on axle");
                wheelVisual.localPosition = axle;
            }

            // The placeholder wheel was a squashed cylinder. Make it an empty holder.
            RemoveComponent<MeshRenderer>(wheelVisual.gameObject);
            RemoveComponent<MeshFilter>(wheelVisual.gameObject);
            RemoveComponent<Collider>(wheelVisual.gameObject);
            for (int i = wheelVisual.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(wheelVisual.GetChild(i).gameObject);
            Undo.RecordObject(wheelVisual, "Reset wheel scale");
            wheelVisual.localScale = Vector3.one;

            MeshPart("Tire", wheelVisual, tireMesh, tireMat, Vector3.zero, Vector3.zero);
            Cyl("Rim", wheelVisual, white, Vector3.zero, 0.09f, 0.05f, Vector3.zero);
            Cyl("Hub", wheelVisual, yellow, Vector3.zero, 0.03f, 0.062f, Vector3.zero);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    Box("Spoke", wheelVisual, yellow, new Vector3(0f, side * 0.0255f, 0f),
                        new Vector3(0.013f, 0.004f, 0.16f), new Vector3(0f, i * 36f, 0f));
                }
            }
        }

        // Lemon slice: yellow rim, white face, six yellow segment lines. Faces sideways.
        private static void BuildEmblem(Transform parent, Material yellow, Material white, Vector3 center, float sideSign)
        {
            Cyl("EmblemRing", parent, yellow, center, 0.042f, 0.004f, new Vector3(0f, 0f, 90f));
            Cyl("EmblemFace", parent, white, center + new Vector3(sideSign * 0.0012f, 0f, 0f), 0.033f, 0.004f, new Vector3(0f, 0f, 90f));
            for (int i = 0; i < 6; i++)
            {
                Box("EmblemSegment", parent, yellow, center + new Vector3(sideSign * 0.0025f, 0f, 0f),
                    new Vector3(0.004f, 0.062f, 0.004f), new Vector3(i * 30f, 0f, 0f));
            }
            Cyl("EmblemCenter", parent, yellow, center + new Vector3(sideSign * 0.0028f, 0f, 0f), 0.006f, 0.004f, new Vector3(0f, 0f, 90f));
        }

        // ------------------------------------------------------------------
        // Primitive helpers. Positions are in scene-local coordinates of the
        // scooter; the current origin is subtracted so the steering assembly can
        // be built around its own pivot.
        // ------------------------------------------------------------------
        private static Transform Part(string name, Transform parent, PrimitiveType type, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);   // visual only
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Transform Box(string name, Transform parent, Material material, Vector3 center, Vector3 size, Vector3 euler = default)
        {
            Transform t = Part(name, parent, PrimitiveType.Cube, material);
            t.localPosition = center - origin;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = size;
            return t;
        }

        // Cylinder along its own local Y axis (then rotated by euler), with a real radius and length.
        private static Transform Cyl(string name, Transform parent, Material material, Vector3 center, float radius, float length, Vector3 euler)
        {
            Transform t = Part(name, parent, PrimitiveType.Cylinder, material);
            t.localPosition = center - origin;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            return t;
        }

        private static Transform Sphere(string name, Transform parent, Material material, Vector3 center, Vector3 size)
        {
            Transform t = Part(name, parent, PrimitiveType.Sphere, material);
            t.localPosition = center - origin;
            t.localScale = size;
            return t;
        }

        // A cylinder running between two points.
        private static Transform Strut(string name, Transform parent, Material material, Vector3 a, Vector3 b, float radius)
        {
            Vector3 dir = b - a;
            Transform t = Part(name, parent, PrimitiveType.Cylinder, material);
            t.localPosition = (a + b) * 0.5f - origin;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            t.localScale = new Vector3(radius * 2f, dir.magnitude * 0.5f, radius * 2f);
            return t;
        }

        private static Transform MeshPart(string name, Transform parent, Mesh mesh, Material material, Vector3 center, Vector3 euler)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center - origin;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        // ------------------------------------------------------------------
        // Generated meshes
        // ------------------------------------------------------------------

        // Torus around the Y axis: the tire. Smooth shaded.
        private static Mesh BuildTorus(string name, float majorRadius, float minorRadius, int around, int tube)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i <= around; i++)
            {
                float u = i / (float)around * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                for (int j = 0; j <= tube; j++)
                {
                    float v = j / (float)tube * Mathf.PI * 2f;
                    Vector3 normal = ring * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                    verts.Add(ring * majorRadius + normal * minorRadius);
                    norms.Add(normal);
                }
            }

            int stride = tube + 1;
            for (int i = 0; i < around; i++)
            {
                for (int j = 0; j < tube; j++)
                {
                    int a = i * stride + j;
                    int b = a + stride;
                    int c = b + 1;
                    int d = a + 1;
                    AddOutwardTriangle(verts, norms, tris, a, b, c);
                    AddOutwardTriangle(verts, norms, tris, a, c, d);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Keeps a triangle facing the way the vertex normals point, whatever order it was built in.
        private static void AddOutwardTriangle(List<Vector3> verts, List<Vector3> norms, List<int> tris, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            Vector3 hint = norms[a] + norms[b] + norms[c];
            if (Vector3.Dot(face, hint) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        // A curved fender: a thin shell that wraps part of a wheel. phi 0 is straight forward
        // (+Z), 90 is straight up, 180 is straight back. The shell lies in the YZ plane around
        // the X axis through the origin, so place the mesh at the axle. Flat shaded, built
        // double sided so it cannot be seen inside out.
        private static Mesh BuildArcShell(string name, float radius, float width, float thickness, float startDeg, float endDeg, int segments)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float half = width * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float p0 = Mathf.Lerp(startDeg, endDeg, i / (float)segments) * Mathf.Deg2Rad;
                float p1 = Mathf.Lerp(startDeg, endDeg, (i + 1) / (float)segments) * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(0f, Mathf.Sin(p0), Mathf.Cos(p0));
                Vector3 d1 = new Vector3(0f, Mathf.Sin(p1), Mathf.Cos(p1));
                float outer = radius + thickness;

                // outer and inner surfaces
                AddQuadBothSides(verts, norms, tris,
                    d0 * outer + Vector3.left * half, d0 * outer + Vector3.right * half,
                    d1 * outer + Vector3.right * half, d1 * outer + Vector3.left * half, (d0 + d1));
                AddQuadBothSides(verts, norms, tris,
                    d0 * radius + Vector3.left * half, d0 * radius + Vector3.right * half,
                    d1 * radius + Vector3.right * half, d1 * radius + Vector3.left * half, -(d0 + d1));

                // side edges
                AddQuadBothSides(verts, norms, tris,
                    d0 * radius + Vector3.left * half, d0 * outer + Vector3.left * half,
                    d1 * outer + Vector3.left * half, d1 * radius + Vector3.left * half, Vector3.left);
                AddQuadBothSides(verts, norms, tris,
                    d0 * radius + Vector3.right * half, d0 * outer + Vector3.right * half,
                    d1 * outer + Vector3.right * half, d1 * radius + Vector3.right * half, Vector3.right);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuadBothSides(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outwardHint)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, outwardHint) < 0f)
            {
                // Flip the winding so the first copy faces the hint.
                Vector3 swap = b; b = d; d = swap;
                normal = -normal;
            }

            int start = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 4; i++) norms.Add(normal);
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);

            // Back copy, facing the other way, so the shell looks solid from both sides.
            int back = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 4; i++) norms.Add(-normal);
            tris.Add(back); tris.Add(back + 2); tris.Add(back + 1);
            tris.Add(back); tris.Add(back + 3); tris.Add(back + 2);
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = MeshFolder + "/" + mesh.name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // ------------------------------------------------------------------
        // Small utilities
        // ------------------------------------------------------------------
        private static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Vector3 AxlePosition(SerializedObject controller, string property, Vector3 fallback)
        {
            var wheel = controller.FindProperty(property).objectReferenceValue as Component;
            return wheel != null ? wheel.transform.localPosition : fallback;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
        }

        private static void RemoveChildren(Transform parent, string childName)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static void RemoveComponent<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            if (component != null) Undo.DestroyObjectImmediate(component);
        }
    }
}
