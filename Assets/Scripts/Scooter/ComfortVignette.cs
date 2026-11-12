using UnityEngine;
using UnityEngine.InputSystem;

namespace CommodoreCircuit.Scooter
{
    /// <summary>
    /// VR comfort vignette. Blacks out the edges of your view while you accelerate, boost or
    /// turn hard, and eases back off when you coast, because the mismatch between what your eyes
    /// see (fast motion) and what your body feels (sitting still) is what makes driving nauseating.
    ///
    /// It creates itself when the game starts (no scene or prefab setup) and attaches a ring mesh to
    /// the head camera. The ring only shows once the strength is above zero, so it costs nothing at rest.
    ///
    /// Strength is built from: how fast you are going (a little, so cruising keeps a light tunnel),
    /// how hard you are accelerating or braking (the most), how fast you are turning, and boost.
    /// It rises quickly and fades slowly, which is the gentle direction for your eyes.
    ///
    /// Settings: Off / Low / High, set from the pause menu or with <see cref="SetLevel"/> / <see cref="CycleLevel"/> (V key on desktop).
    /// The level is remembered between runs. Other systems (e.g. a third-person camera) can set
    /// <see cref="Suppressed"/> to hide it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ComfortVignette : MonoBehaviour
    {
        public enum Level { Off, Low, High }

        // New key: the old one stored four levels (Off/Low/Medium/High), so its numbers meant something else.
        private const string LevelKey = "CommodoreCircuit.ComfortLevel2";
        // Low is a light tunnel, High is a tight one. Applied before the curve below.
        private static readonly float[] LevelScale = { 0f, 1f, 1.6f };
        private const float MaxShown = 1.1f;

        public static ComfortVignette Instance { get; private set; }
        public static Level CurrentLevel { get; private set; } = Level.High;
        public static event System.Action<Level> LevelChanged;
        /// <summary>Set true to hide the vignette (e.g. while a third-person camera is active).</summary>
        public static bool Suppressed { get; set; }
        /// <summary>Holds the vignette closed so a menu can show what the chosen level looks like.</summary>
        public static bool Preview { get; set; }
        /// <summary>0..1 current strength after the level multiplier, for debugging or UI.</summary>
        public static float CurrentStrength { get; private set; }

        public static void SetLevel(Level level)
        {
            CurrentLevel = level;
            try { PlayerPrefs.SetInt(LevelKey, (int)level); } catch { /* prefs unavailable: keep in memory */ }
            LevelChanged?.Invoke(level);
        }

        public static void CycleLevel() => SetLevel((Level)(((int)CurrentLevel + 1) % 3));

        [Header("What drives it")]
        [Tooltip("Strength from simply going fast (at top speed). Keep low so cruising stays comfortable and open.")]
        [SerializeField, Range(0f, 1f)] private float speedWeight = 0.45f;
        [Tooltip("Strength from accelerating (braking counts half). This is the main trigger.")]
        [SerializeField, Range(0f, 1f)] private float accelerationWeight = 0.8f;
        [Tooltip("Acceleration (m/s per second) that counts as 'full' acceleration.")]
        [SerializeField, Min(1f)] private float accelerationReference = 10f;
        [Tooltip("Extra strength from turning hard at speed.")]
        [SerializeField, Range(0f, 1f)] private float turnWeight = 0.35f;
        [Tooltip("Turn rate (degrees per second) that counts as 'full' turning.")]
        [SerializeField, Min(10f)] private float turnReference = 80f;
        [Tooltip("Extra strength while a drift boost is running.")]
        [SerializeField, Range(0f, 1f)] private float boostWeight = 0.3f;

        [Header("Feel")]
        [Tooltip("How fast the vignette closes in (strength per second).")]
        [SerializeField, Min(0.1f)] private float attackRate = 3.5f;
        [Tooltip("How fast it opens back up (strength per second). Slower than attack on purpose.")]
        [SerializeField, Min(0.05f)] private float releaseRate = 0.7f;

        [Header("Shape")]
        [Tooltip("Half-angle of the clear area at full strength (degrees from the centre of your view). Smaller = tighter tunnel.")]
        [SerializeField, Range(10f, 70f)] private float fullStrengthClearAngle = 24f;
        [Tooltip("How soft the edge is (degrees from fully clear to fully black).")]
        [SerializeField, Range(5f, 45f)] private float featherAngle = 20f;
        [Tooltip("How far in front of the camera the ring sits (m). Must be past the camera's near clip.")]
        [SerializeField, Min(0.31f)] private float ringDistance = 0.4f;

        private const int Segments = 48;
        private const float ClearAngle = 72f;   // clear area at strength 0: outside what a Quest can show
        private const float MaxAngle = 86f;

        private ScooterController scooter;
        private float nextFind;
        private float lastSpeed, smoothedAccel, lastYaw, smoothedTurn;
        private bool hasLast;
        private float strength;

        private Transform ring;
        private MeshRenderer ringRenderer;
        private Mesh mesh;
        private Material material;
        private Vector3[] verts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Suppressed = false;
            Preview = false;
            CurrentStrength = 0f;
            LevelChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Comfort Vignette");
            DontDestroyOnLoad(go);
            go.AddComponent<ComfortVignette>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            try { CurrentLevel = (Level)Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, (int)Level.High), 0, 2); }
            catch { CurrentLevel = Level.High; }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ring) Destroy(ring.gameObject);
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame) CycleLevel();

            if (scooter == null)
            {
                hasLast = false;
                if (Time.unscaledTime >= nextFind)
                {
                    nextFind = Time.unscaledTime + 0.5f;
                    var local = Race.RaceProgress.Local;
                    scooter = local ? local.Scooter : FindAnyObjectByType<ScooterController>();
                }
            }

            Camera cam = Camera.main;
            bool active = scooter != null && cam != null && !Suppressed && CurrentLevel != Level.Off;

            float target = 0f;
            if (active)
            {
                // The scooter keeps its speed in the physics step, so smooth the changes between frames.
                float speed = Mathf.Abs(scooter.Speed);
                float yaw = scooter.transform.eulerAngles.y;
                if (!hasLast) { lastSpeed = speed; lastYaw = yaw; hasLast = true; }

                float accel = (speed - lastSpeed) / dt;
                float turn = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / dt;
                lastSpeed = speed;
                lastYaw = yaw;

                float k = 1f - Mathf.Exp(-10f * dt);
                smoothedAccel = Mathf.Lerp(smoothedAccel, accel, k);
                smoothedTurn = Mathf.Lerp(smoothedTurn, Mathf.Min(turn, turnReference * 2f), k);

                float speedFrac = Mathf.Clamp01(speed / Mathf.Max(1f, scooter.MaxSpeed));
                float accelFrac = Mathf.Clamp01((smoothedAccel > 0f ? smoothedAccel : -smoothedAccel * 0.5f) / accelerationReference);
                float turnFrac = Mathf.Clamp01(smoothedTurn / turnReference) * speedFrac;

                target = speedFrac * speedWeight
                       + accelFrac * accelerationWeight
                       + turnFrac * turnWeight
                       + (scooter.IsBoosting ? boostWeight : 0f);
                target = Mathf.Clamp01(target);
            }

            if (Preview && cam != null && !Suppressed) target = 0.55f;
            strength = Mathf.MoveTowards(strength, target, (target > strength ? attackRate : releaseRate) * dt);
            // Square root: the first bit of strength already pulls the edge into view, so it is easy to notice.
            float shown = Mathf.Min(Mathf.Sqrt(Mathf.Max(0f, strength * LevelScale[(int)CurrentLevel])), MaxShown);
            CurrentStrength = shown;

            if (shown < 0.01f || cam == null)
            {
                if (ringRenderer) ringRenderer.enabled = false;
                return;
            }

            if (!EnsureRing(cam)) return;
            ringRenderer.enabled = true;
            Shape(shown, Mathf.Max(ringDistance, cam.nearClipPlane + 0.05f));
        }

        private bool EnsureRing(Camera cam)
        {
            if (ring != null)
            {
                if (ring.parent != cam.transform)
                {
                    ring.SetParent(cam.transform, false);
                    ring.localPosition = Vector3.zero;
                    ring.localRotation = Quaternion.identity;
                }
                return true;
            }

            var shader = Resources.Load<Shader>("ComfortVignette");
            if (shader == null) shader = Shader.Find("CommodoreCircuit/ComfortVignette");
            if (shader == null)
            {
                Debug.LogWarning("ComfortVignette: shader 'ComfortVignette' not found in Assets/Resources. Vignette disabled.");
                enabled = false;
                return false;
            }

            if (material == null) material = new Material(shader) { name = "Comfort Vignette (runtime)" };
            BuildMesh();

            var go = new GameObject("Comfort Vignette Ring");
            ring = go.transform;
            ring.SetParent(cam.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            ringRenderer = go.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            ringRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            ringRenderer.allowOcclusionWhenDynamic = false;
            return true;
        }

        // Three rings of vertices: inner edge (clear), outer edge of the soft band (black),
        // and a far ring (black) so nothing shows past the edge of the view.
        private void BuildMesh()
        {
            if (mesh != null) return;
            mesh = new Mesh { name = "Comfort Vignette Ring" };
            mesh.MarkDynamic();

            verts = new Vector3[Segments * 3];
            var colors = new Color32[Segments * 3];
            var tris = new int[Segments * 12];
            for (int i = 0; i < Segments; i++)
            {
                colors[i] = new Color32(0, 0, 0, 0);
                colors[Segments + i] = new Color32(0, 0, 0, 255);
                colors[Segments * 2 + i] = new Color32(0, 0, 0, 255);

                int j = (i + 1) % Segments;
                int t = i * 12;
                tris[t + 0] = i;             tris[t + 1] = j;             tris[t + 2] = Segments + j;
                tris[t + 3] = i;             tris[t + 4] = Segments + j;  tris[t + 5] = Segments + i;
                tris[t + 6] = Segments + i;  tris[t + 7] = Segments + j;  tris[t + 8] = Segments * 2 + j;
                tris[t + 9] = Segments + i;  tris[t + 10] = Segments * 2 + j; tris[t + 11] = Segments * 2 + i;
            }
            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        }

        // strength 0 = clear area wider than any headset's view, 1 = tight tunnel.
        private void Shape(float s, float d)
        {
            float a0 = Mathf.LerpUnclamped(ClearAngle, fullStrengthClearAngle, s);
            float a1 = Mathf.Min(a0 + featherAngle, MaxAngle);
            float r0 = d * Mathf.Tan(a0 * Mathf.Deg2Rad);
            float r1 = d * Mathf.Tan(a1 * Mathf.Deg2Rad);
            float rFar = Mathf.Max(r1 * 1.5f, 6f);

            for (int i = 0; i < Segments; i++)
            {
                float ang = i * (Mathf.PI * 2f / Segments);
                float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                verts[i] = new Vector3(cx * r0, cy * r0, d);
                verts[Segments + i] = new Vector3(cx * r1, cy * r1, d);
                verts[Segments * 2 + i] = new Vector3(cx * rFar, cy * rFar, d);
            }
            mesh.vertices = verts;
        }
    }
}
