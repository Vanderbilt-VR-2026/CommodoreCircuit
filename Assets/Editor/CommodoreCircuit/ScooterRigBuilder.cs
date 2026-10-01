using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using CommodoreCircuit.Scooter;

namespace CommodoreCircuit.EditorTools
{
    /// <summary>
    /// Editor tooling for the scooter test rig.
    ///
    /// Menu commands under CommodoreCircuit:
    ///   - "Add VR Rig to Selected Scooter" - the one to use once you already
    ///     have a working scooter in the scene (built or hand-tuned) and just
    ///     want VR hand tracking wired to it. Select the scooter's root
    ///     GameObject (the one with ScooterController) first.
    ///   - "Build Placeholder Scooter (Keyboard Only)" - builds a brand new
    ///     placeholder scooter (box body + two WheelColliders), no VR rig.
    ///   - "Build Placeholder Scooter + VR Rig" - same, but with the VR rig
    ///     included from the start. Use this only when starting fresh -
    ///     otherwise prefer "Add VR Rig to Selected Scooter" so you don't
    ///     lose tuning on an existing scooter.
    /// </summary>
    public static class ScooterRigBuilder
    {
        [MenuItem("CommodoreCircuit/Add VR Rig to Selected Scooter")]
        public static void AddVRRigToSelectedScooter()
        {
            GameObject scooterGO = Selection.activeGameObject;
            ScooterController controller = scooterGO != null ? scooterGO.GetComponent<ScooterController>() : null;

            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Select the scooter first",
                    "Select your scooter's root GameObject in the Hierarchy (the one with the " +
                    "ScooterController component) and run this command again.",
                    "OK");
                return;
            }

            BuildVRRig(scooterGO.transform, out Transform leftHand, out Transform rightHand);

            var so = new SerializedObject(controller);
            so.FindProperty("leftHandAnchor").objectReferenceValue = leftHand;
            so.FindProperty("rightHandAnchor").objectReferenceValue = rightHand;
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = scooterGO;

            EditorUtility.DisplayDialog(
                "VR rig attached",
                "Hand anchors created under " + scooterGO.name + " and wired into its ScooterController.\n\n" +
                "Before it responds to a headset:\n" +
                "1. Project Settings > XR Plug-in Management > OpenXR - add the interaction profile " +
                "for your controllers (e.g. Meta Quest Touch Plus / Oculus Touch).\n" +
                "2. Project Settings > XR Plug-in Management - enable 'Initialize XR on Startup' for " +
                "your target platform.\n\n" +
                "Keyboard controls (WASD / arrows) still work too.",
                "OK");
        }

        [MenuItem("CommodoreCircuit/Add VR Rig to Selected Scooter", true)]
        public static bool ValidateAddVRRigToSelectedScooter()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ScooterController>() != null;
        }

        [MenuItem("CommodoreCircuit/Build Placeholder Scooter (Keyboard Only)")]
        public static void BuildKeyboardOnly()
        {
            BuildScooterCore(includeVRRig: false);

            EditorUtility.DisplayDialog(
                "Scooter rig created",
                "Placeholder scooter built. Press Play and drive with:\n\n" +
                "W / Up Arrow    - accelerate\n" +
                "S / Down Arrow  - brake / drift\n" +
                "A/D or Left/Right - steer",
                "OK");
        }

        [MenuItem("CommodoreCircuit/Build Placeholder Scooter + VR Rig")]
        public static void BuildWithVRRig()
        {
            BuildScooterCore(includeVRRig: true);

            EditorUtility.DisplayDialog(
                "Scooter rig created",
                "Placeholder scooter + VR hand rig built.\n\n" +
                "Before it will actually respond to a headset:\n" +
                "1. Project Settings > XR Plug-in Management > OpenXR - add the " +
                "interaction profile for your controllers (e.g. Meta Quest Touch " +
                "Plus / Oculus Touch), for both PC and Android tabs as needed.\n" +
                "2. Project Settings > XR Plug-in Management - make sure " +
                "'Initialize XR on Startup' is checked for your target platform " +
                "(it was off in this project).\n\n" +
                "Keyboard controls (WASD / arrows) still work too.",
                "OK");
        }

        private static GameObject BuildScooterCore(bool includeVRRig)
        {
            var scooter = new GameObject("Scooter");
            Undo.RegisterCreatedObjectUndo(scooter, "Create Scooter");

            Rigidbody rb = scooter.AddComponent<Rigidbody>();
            rb.mass = 45f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;

            BuildBody(scooter.transform);

            GameObject frontWheelGO = CreateWheelCollider(scooter.transform, "FrontWheel_Collider", new Vector3(0f, 0.15f, 0.6f));
            GameObject rearWheelGO = CreateWheelCollider(scooter.transform, "RearWheel_Collider", new Vector3(0f, 0.15f, -0.6f));

            Transform frontVisual = CreateWheelVisual(scooter.transform, "FrontWheel_Visual");
            Transform rearVisual = CreateWheelVisual(scooter.transform, "RearWheel_Visual");

            Transform leftHand = null;
            Transform rightHand = null;
            if (includeVRRig)
            {
                BuildVRRig(scooter.transform, out leftHand, out rightHand);
            }

            ScooterController controller = scooter.AddComponent<ScooterController>();
            WireController(controller, frontWheelGO, rearWheelGO, frontVisual, rearVisual, leftHand, rightHand);

            Selection.activeGameObject = scooter;
            return scooter;
        }

        private static void BuildBody(Transform parent)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body_Visual";
            body.transform.SetParent(parent);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.transform.localScale = new Vector3(0.25f, 0.5f, 1.3f);
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());

            BoxCollider bodyCollider = parent.gameObject.AddComponent<BoxCollider>();
            bodyCollider.center = new Vector3(0f, 0.5f, 0f);
            bodyCollider.size = new Vector3(0.35f, 1.0f, 1.4f);
        }

        private static GameObject CreateWheelCollider(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;

            WheelCollider wc = go.AddComponent<WheelCollider>();
            wc.radius = 0.15f;
            wc.mass = 8f;
            wc.suspensionDistance = 0.12f;

            JointSpring spring = wc.suspensionSpring;
            spring.spring = 8000f;
            spring.damper = 500f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;

            WheelFrictionCurve forward = wc.forwardFriction;
            forward.extremumSlip = 0.4f;
            forward.extremumValue = 1f;
            forward.asymptoteSlip = 0.8f;
            forward.asymptoteValue = 0.5f;
            forward.stiffness = 1.5f;
            wc.forwardFriction = forward;

            WheelFrictionCurve sideways = wc.sidewaysFriction;
            sideways.extremumSlip = 0.2f;
            sideways.extremumValue = 1f;
            sideways.asymptoteSlip = 0.5f;
            sideways.asymptoteValue = 0.75f;
            sideways.stiffness = 1.2f;
            wc.sidewaysFriction = sideways;

            return go;
        }

        private static Transform CreateWheelVisual(Transform parent, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(0.3f, 0.03f, 0.3f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static void BuildVRRig(Transform parent, out Transform leftHand, out Transform rightHand)
        {
            var rig = new GameObject("VRRig");
            Undo.RegisterCreatedObjectUndo(rig, "Create VR Rig");
            rig.transform.SetParent(parent);
            rig.transform.localPosition = new Vector3(0f, 1.1f, 0.1f);

            // Attach the scene's existing Main Camera as the tracked head, if
            // there is one. Otherwise fall back to a plain anchor.
            GameObject head = Camera.main != null ? Camera.main.gameObject : new GameObject("HeadAnchor");
            if (Camera.main == null)
            {
                Undo.RegisterCreatedObjectUndo(head, "Create Head Anchor");
            }
            Undo.SetTransformParent(head.transform, rig.transform, "Attach head to VR rig");
            head.transform.localPosition = Vector3.zero;
            head.transform.localRotation = Quaternion.identity;
            AddTrackedPose(head, "Center Eye Position", "Center Eye Rotation",
                "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

            var left = new GameObject("LeftHandAnchor");
            Undo.RegisterCreatedObjectUndo(left, "Create Left Hand Anchor");
            left.transform.SetParent(rig.transform);
            left.transform.localPosition = new Vector3(-0.3f, -0.1f, 0.3f);
            AddTrackedPose(left, "Left Hand Position", "Left Hand Rotation",
                "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation");

            var right = new GameObject("RightHandAnchor");
            Undo.RegisterCreatedObjectUndo(right, "Create Right Hand Anchor");
            right.transform.SetParent(rig.transform);
            right.transform.localPosition = new Vector3(0.3f, -0.1f, 0.3f);
            AddTrackedPose(right, "Right Hand Position", "Right Hand Rotation",
                "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation");

            leftHand = left.transform;
            rightHand = right.transform;
        }

        private static void AddTrackedPose(
            GameObject go,
            string positionActionName,
            string rotationActionName,
            string positionBinding,
            string rotationBinding)
        {
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(new InputAction(positionActionName, InputActionType.Value, positionBinding));
            driver.rotationInput = new InputActionProperty(new InputAction(rotationActionName, InputActionType.Value, rotationBinding));
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        private static void WireController(
            ScooterController controller,
            GameObject frontWheelGO,
            GameObject rearWheelGO,
            Transform frontVisual,
            Transform rearVisual,
            Transform leftHand,
            Transform rightHand)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("frontWheel").objectReferenceValue = frontWheelGO.GetComponent<WheelCollider>();
            so.FindProperty("rearWheel").objectReferenceValue = rearWheelGO.GetComponent<WheelCollider>();
            so.FindProperty("frontWheelVisual").objectReferenceValue = frontVisual;
            so.FindProperty("rearWheelVisual").objectReferenceValue = rearVisual;
            so.FindProperty("leftHandAnchor").objectReferenceValue = leftHand;
            so.FindProperty("rightHandAnchor").objectReferenceValue = rightHand;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
