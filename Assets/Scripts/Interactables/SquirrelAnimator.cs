using UnityEngine;
using static CommodoreCircuit.Interactables.InteractableUtil;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Procedural squirrel animation, no rig needed: the model's Body, Head and
    /// Tail_1..3 are separate pivoted parts.
    ///   Idle   - lazy tail sway, breathing
    ///   Tell   - tail flicks + head chitter (the 0.5 s warning)
    ///   Moving - hop-run, tail streams behind
    ///   Hit    - comic tumble
    /// Sits on the Mover next to <see cref="Hazard"/>; reads state from the parent <see cref="PathMover"/>.
    /// </summary>
    public class SquirrelAnimator : MonoBehaviour
    {
        public PathMover path;
        public Hazard hazard;
        [Tooltip("The imported model root (child of Mover). Found by name if empty.")]
        public Transform model;

        public float hopHeight = 0.18f;
        public float hopsPerMetre = 1.3f;
        public float tumbleDuration = 0.7f;

        Rest body, head, tail1, tail2, tail3, root;
        float distance;
        float tumbleT = -1f;

        void Awake()
        {
            if (path == null) path = GetComponentInParent<PathMover>();
            if (hazard == null) hazard = GetComponent<Hazard>();
            if (model == null) model = FindDeep(transform, "Squirrel");
            root = new Rest(model);
            body = new Rest(FindDeep(model, "Body"));
            head = new Rest(FindDeep(model, "Head"));
            tail1 = new Rest(FindDeep(model, "Tail_1"));
            tail2 = new Rest(FindDeep(model, "Tail_2"));
            tail3 = new Rest(FindDeep(model, "Tail_3"));
        }

        void OnEnable() { if (hazard != null) hazard.Hit += OnHit; }
        void OnDisable() { if (hazard != null) hazard.Hit -= OnHit; }

        void OnHit(Transform racer) => tumbleT = 0f;

        void Update()
        {
            float t = Time.time;
            var state = path != null ? path.State : PathMover.MoveState.Idle;

            // Defaults: idle.
            float sway = Mathf.Sin(t * 2.2f) * 7f;
            float tailPitch = Mathf.Sin(t * 1.4f) * 4f;
            float headPitch = 0f, headYaw = Mathf.Sin(t * 0.7f) * 12f;
            float bodyLift = Mathf.Sin(t * 3f) * 0.006f;
            float bodyPitch = 0f;

            if (state == PathMover.MoveState.Tell)
            {
                tailPitch = Mathf.Sin(t * 26f) * 20f;      // flick flick flick
                headPitch = Mathf.Sin(t * 34f) * 6f;       // chitter
                headYaw = 0f;
            }
            else if (state == PathMover.MoveState.Moving)
            {
                distance += path.CurrentSpeed * Time.deltaTime;
                float hop = Mathf.Abs(Mathf.Sin(distance * Mathf.PI * hopsPerMetre));
                bodyLift = hop * hopHeight;
                bodyPitch = 18f;                           // lean into the run
                tailPitch = -28f + hop * 14f;              // tail streams back
                sway = Mathf.Sin(distance * 3f) * 5f;
                headYaw = 0f;
                headPitch = -12f;
            }

            body.Pose(new Vector3(bodyPitch, 0f, 0f), new Vector3(0f, bodyLift, 0f));
            head.Pose(new Vector3(headPitch, headYaw, 0f));
            tail1.Pose(new Vector3(tailPitch * 0.5f, 0f, sway));
            tail2.Pose(new Vector3(tailPitch * 0.8f, 0f, sway * 1.3f));
            tail3.Pose(new Vector3(tailPitch, 0f, sway * 1.6f));

            // Tumble on hit: one full roll plus a bounce, then back on its feet.
            if (tumbleT >= 0f)
            {
                tumbleT += Time.deltaTime;
                float k = Mathf.Clamp01(tumbleT / tumbleDuration);
                root.Pose(new Vector3(0f, 0f, 360f * Mathf.SmoothStep(0f, 1f, k)),
                          new Vector3(0f, Mathf.Sin(k * Mathf.PI) * 0.35f, 0f));
                if (k >= 1f) { tumbleT = -1f; root.Pose(Vector3.zero); }
            }
        }
    }
}
