using System.Collections.Generic;
using UnityEngine;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// Third-person mode. The camera sits behind and above the scooter and you steer with the
    /// thumbsticks instead of the handlebar: either stick left/right turns, the left trigger is
    /// forward, the right trigger is backward/brake, and either grip button (or A / X) drifts.
    ///
    /// Switch it from the pause menu (View row), or with <see cref="SetEnabled"/> / <see cref="Toggle"/>
    /// (T key on desktop). The choice is remembered between runs.
    ///
    /// It creates itself when the game starts. While it is on it:
    ///   - moves the VR rig (head and hands) back and up as a chase camera. It is NOT locked to the
    ///     scooter's heading: small steering wiggles don't move it, it only swings around once you turn
    ///     past a dead zone (a bend in the road, or a bigger turn), and it eases back behind you when you
    ///     straighten out. Height is smoothed too, so bumps don't jolt the view;
    ///   - hides the controller models and handlebar, which would float in front of the camera;
    ///   - shows a placeholder rider on the scooter (to be replaced by the real characters);
    ///   - tells the scooter to use thumbstick steering (<see cref="ScooterController.ThirdPersonControls"/>).
    /// Other players still see you riding the scooter: NetworkRacer sends the seated eye position
    /// while this is on.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class ThirdPersonView : MonoBehaviour
    {
        private const string PrefKey = "CommodoreCircuit.ThirdPerson";

        public static ThirdPersonView Instance { get; private set; }
        public static bool Enabled { get; private set; }
        public static event System.Action<bool> Changed;

        public static void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            try { PlayerPrefs.SetInt(PrefKey, on ? 1 : 0); } catch { /* prefs unavailable: keep in memory */ }
            Changed?.Invoke(on);
        }

        public static void Toggle() => SetEnabled(!Enabled);

        /// <summary>
        /// Holds the camera exactly where it is (the pause menu sets this while it is open), so the
        /// view doesn't keep easing around and drift away from the menu.
        /// </summary>
        public static bool Frozen { get; set; }

        [Header("Camera")]
        [Tooltip("How far behind the rider the camera sits (m).")]
        [SerializeField, Min(0.5f)] private float distanceBehind = 3.6f;
        [Tooltip("How far above the rider's eye height the camera sits (m).")]
        [SerializeField] private float heightAbove = 1.0f;

        [Header("Follow (keeps the view calm)")]
        [Tooltip("The scooter can turn this many degrees either side before the camera starts to follow. Small steering movements stay inside it, so the view doesn't move.")]
        [SerializeField, Range(0f, 45f)] private float followDeadzone = 12f;
        [Tooltip("How quickly the camera catches up with the part of a turn beyond the dead zone (per second). Lower = lazier.")]
        [SerializeField, Min(0.1f)] private float followRate = 1.6f;
        [Tooltip("How slowly the camera drifts back to straight behind you once you stop turning (per second).")]
        [SerializeField, Min(0f)] private float recenterRate = 0.35f;
        [Tooltip("The camera never swings around faster than this (degrees per second).")]
        [SerializeField, Min(10f)] private float maxFollowSpeed = 60f;
        [Tooltip("Smoothing for the camera's height, so small bumps in the ground don't jolt the view (seconds).")]
        [SerializeField, Min(0.05f)] private float heightSmoothTime = 0.3f;

        [Header("Placeholder rider")]
        [SerializeField] private Color skinColor = new Color(0.86f, 0.68f, 0.45f);
        [SerializeField] private Color shirtColor = new Color(0.27f, 0.22f, 0.55f);

        private const float DeckHeight = 0.25f;
        private const float NeckDrop = 0.22f;

        private ScooterController scooter;
        private float nextFind;
        private float nextGearScan;

        private bool applied;
        private ScooterController appliedScooter;
        private Transform appliedRig;
        private Vector3 firstPersonLocal;
        private Quaternion firstPersonLocalRotation;
        private float cameraYaw, smoothedHeight, heightVelocity;
        private Vector3 lastScooterPosition;
        private readonly List<Renderer> hidden = new List<Renderer>();
        private GameObject riderAvatar;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Enabled = false;
            Frozen = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Third Person View");
            DontDestroyOnLoad(go);
            go.AddComponent<ThirdPersonView>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            try { Enabled = PlayerPrefs.GetInt(PrefKey, 0) == 1; } catch { Enabled = false; }
        }

        private void OnDestroy()
        {
            Release();
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame) Toggle();

            if (scooter == null && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 0.5f;
                var local = Race.RaceProgress.Local;
                scooter = local ? local.Scooter : FindAnyObjectByType<ScooterController>();
            }

            // A new scene brings a new scooter: let go of the old one.
            if (applied && scooter != appliedScooter) Release();

            Transform rig = scooter ? ResolveRig() : null;
            bool want = Enabled && scooter != null && rig != null;
            if (want)
            {
                if (!applied || rig != appliedRig) Apply(rig);
                else if (Time.unscaledTime >= nextGearScan)
                {
                    // Controller and hand models can appear a moment after the rig does.
                    nextGearScan = Time.unscaledTime + 1f;
                    HideGear(rig);
                }
            }
            else if (applied)
            {
                Release();
            }

            if (applied) FollowScooter(Mathf.Min(Time.deltaTime, 0.05f));
        }

        // Chase camera. The yaw lags behind the scooter's heading: nothing happens inside the
        // dead zone, past it the camera follows smoothly, and when the scooter holds a heading the
        // camera slowly drifts to sit straight behind it.
        private void FollowScooter(float dt)
        {
            if (appliedRig == null || appliedScooter == null) return;
            Transform body = appliedScooter.transform;
            Vector3 position = body.position;
            float heading = body.eulerAngles.y;

            // Respawn or teleport: put the camera behind the scooter at once instead of swinging round.
            if ((position - lastScooterPosition).sqrMagnitude > 25f)
            {
                cameraYaw = heading;
                smoothedHeight = position.y;
                heightVelocity = 0f;
            }
            lastScooterPosition = position;

            if (Frozen) dt = 0f;
            if (dt > 0f)
            {
                float diff = Mathf.DeltaAngle(cameraYaw, heading);
                float beyond = Mathf.Sign(diff) * Mathf.Max(0f, Mathf.Abs(diff) - followDeadzone);
                float step = beyond * (1f - Mathf.Exp(-followRate * dt))
                           + diff * (1f - Mathf.Exp(-recenterRate * dt));
                float limit = maxFollowSpeed * dt;
                cameraYaw += Mathf.Clamp(step, -limit, limit);
                smoothedHeight = Mathf.SmoothDamp(smoothedHeight, position.y, ref heightVelocity, heightSmoothTime, Mathf.Infinity, dt);
            }

            Quaternion yaw = Quaternion.Euler(0f, cameraYaw, 0f);
            Vector3 offset = new Vector3(firstPersonLocal.x, firstPersonLocal.y + heightAbove, firstPersonLocal.z - distanceBehind);
            Vector3 world = new Vector3(position.x, smoothedHeight, position.z) + yaw * offset;
            appliedRig.SetPositionAndRotation(world, yaw * firstPersonLocalRotation);
        }

        // With a headset the rig is the head-and-hands parent under the scooter. In the editor
        // without one, the camera itself is moved.
        private Transform ResolveRig()
        {
            if (scooter.RiderRig != null) return scooter.RiderRig;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return null;   // headset still being set up
            var cam = Camera.main;
            return cam != null && cam.transform.IsChildOf(scooter.transform) ? cam.transform : null;
        }

        private void Apply(Transform rig)
        {
            Release();
            appliedScooter = scooter;
            appliedRig = rig;
            firstPersonLocal = rig.localPosition;
            firstPersonLocalRotation = rig.localRotation;
            cameraYaw = scooter.transform.eulerAngles.y;
            smoothedHeight = scooter.transform.position.y;
            heightVelocity = 0f;
            lastScooterPosition = scooter.transform.position;

            scooter.ThirdPersonControls = true;
            HideGear(rig);
            ShowRider(true);
            applied = true;
            FollowScooter(0f);   // place the rig this frame, no one-frame flash at the old spot
        }

        private void Release()
        {
            if (!applied) return;
            applied = false;

            if (appliedRig != null)
            {
                Vector3 p = firstPersonLocal;
                // The rider height may have been set while this was off; the scooter owns that number.
                if (appliedScooter != null && appliedScooter.RiderRig == appliedRig) p.y = appliedScooter.RiderEyeHeight;
                appliedRig.localPosition = p;
                appliedRig.localRotation = firstPersonLocalRotation;
            }
            if (appliedScooter != null) appliedScooter.ThirdPersonControls = false;

            foreach (var r in hidden)
                if (r != null) r.enabled = true;
            hidden.Clear();
            ShowRider(false);

            appliedRig = null;
            appliedScooter = null;
        }

        // Hand and controller models and the handlebar belong to the first-person view.
        private void HideGear(Transform rig)
        {
            if (scooter == null) return;
            HideUnder(scooter.LeftHandAnchor);
            HideUnder(scooter.RightHandAnchor);
            foreach (var bars in scooter.GetComponentsInChildren<ScooterHandlebar>(true))
                HideUnder(bars.transform);

            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r.GetComponentInParent<Camera>() != null) continue;   // the vignette lives under the camera
                if (IsHandOrController(r.transform, rig)) { r.enabled = false; hidden.Add(r); }
            }
        }

        private void HideUnder(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }

        private static bool IsHandOrController(Transform t, Transform rig)
        {
            for (; t != null && t != rig; t = t.parent)
                if (t.name.Contains("Hand") || t.name.Contains("Controller")) return true;
            return false;
        }

        private void ShowRider(bool show)
        {
            if (!show)
            {
                if (riderAvatar) riderAvatar.SetActive(false);
                return;
            }
            if (riderAvatar == null || riderAvatar.transform.parent != scooter.transform)
            {
                if (riderAvatar) Destroy(riderAvatar);
                riderAvatar = BuildRider();
            }
            if (riderAvatar) riderAvatar.SetActive(true);
        }

        // A simple standing rider: capsule body, round head and a dark visor, in a tint of the scooter's own material.
        private GameObject BuildRider()
        {
            Material sample = null;
            foreach (var r in scooter.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial == null || r.GetComponentInParent<Camera>() != null) continue;
                sample = r.sharedMaterial;
                break;
            }
            Material skin = Tinted(sample, skinColor);
            Material shirt = Tinted(sample, shirtColor);
            Material visor = Tinted(sample, new Color(0.08f, 0.08f, 0.1f));

            var root = new GameObject("Third Person Rider").transform;
            root.SetParent(scooter.transform, false);

            float eye = scooter.RiderEyeHeight;
            Vector3 bottom = new Vector3(0f, DeckHeight, -0.1f);
            Vector3 top = new Vector3(0f, eye - NeckDrop, 0f);
            Vector3 span = top - bottom;

            var body = Primitive(PrimitiveType.Capsule, "Body", root, shirt);
            body.localPosition = bottom + span * 0.5f;
            body.localRotation = Quaternion.FromToRotation(Vector3.up, span);
            body.localScale = new Vector3(0.34f, span.magnitude * 0.5f, 0.26f);

            var head = Primitive(PrimitiveType.Sphere, "Head", root, skin);
            head.localPosition = new Vector3(0f, eye, 0f);
            head.localScale = Vector3.one * 0.24f;

            var face = Primitive(PrimitiveType.Cube, "Visor", head, visor);
            face.localPosition = new Vector3(0f, 0.08f, 0.42f);
            face.localScale = new Vector3(0.75f, 0.28f, 0.2f);
            return root.gameObject;
        }

        private static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            // Remove the collider now: the scooter's ground raycast would otherwise hit it for a physics step.
            DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Material Tinted(Material sample, Color c)
        {
            if (sample == null) return null;
            var m = new Material(sample) { name = "Rider " + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            return m;
        }
    }
}
