using UnityEngine;
using static CommodoreCircuit.Interactables.InteractableUtil;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// Procedural lost-freshman animation, no rig needed: legs, arms, torso, head
    /// and the map are separate pivoted parts.
    ///   Walking - slow shuffle, head swivelling (they're lost)
    ///   Paused  - stops at each end of the path to stare at the map
    ///   Hit     - the map goes flying, then they pull out a fresh one
    /// The floating "?" (TellMark) bobs above their head as the always-on tell.
    /// </summary>
    public class FreshmanAnimator : MonoBehaviour
    {
        public PathMover path;
        public Hazard hazard;
        public Transform model;

        public float strideDegrees = 26f;
        public float stepsPerMetre = 2.2f;

        [Header("Map on hit")]
        public float mapFlightDuration = 1.1f;
        public float mapRespawnSeconds = 3f;

        Rest legL, legR, armL, armR, torso, head, map, mark;
        float distance;
        float mapT = -1f;

        void Awake()
        {
            if (path == null) path = GetComponentInParent<PathMover>();
            if (hazard == null) hazard = GetComponent<Hazard>();
            if (model == null) model = FindDeep(transform, "LostFreshman");
            legL = new Rest(FindDeep(model, "Leg_L"));
            legR = new Rest(FindDeep(model, "Leg_R"));
            armL = new Rest(FindDeep(model, "Arm_L"));
            armR = new Rest(FindDeep(model, "Arm_R"));
            torso = new Rest(FindDeep(model, "Torso"));
            head = new Rest(FindDeep(model, "Head"));
            map = new Rest(FindDeep(model, "Map"));
            mark = new Rest(FindDeep(model, "TellMark"));
        }

        void OnEnable() { if (hazard != null) hazard.Hit += OnHit; }
        void OnDisable() { if (hazard != null) hazard.Hit -= OnHit; }

        void OnHit(Transform racer)
        {
            if (mapT < 0f) mapT = 0f;
        }

        void Update()
        {
            float t = Time.time;
            bool walking = path != null && path.State == PathMover.MoveState.Moving;
            if (walking) distance += path.CurrentSpeed * Time.deltaTime;

            float phase = distance * Mathf.PI * stepsPerMetre;
            float stride = walking ? Mathf.Sin(phase) * strideDegrees : 0f;
            legL.Pose(new Vector3(stride, 0f, 0f));
            legR.Pose(new Vector3(-stride, 0f, 0f));
            armL.Pose(new Vector3(walking ? Mathf.Sin(phase) * 4f : 0f, 0f, 0f));
            armR.Pose(new Vector3(walking ? -Mathf.Sin(phase) * 4f : 0f, 0f, 0f));

            float bob = walking ? Mathf.Abs(Mathf.Sin(phase)) * 0.03f : Mathf.Sin(t * 2f) * 0.005f;
            float stumble = mapT >= 0f ? Mathf.Sin(Mathf.Clamp01(mapT / 0.5f) * Mathf.PI) * -14f : 0f;
            torso.Pose(new Vector3(stumble, 0f, walking ? Mathf.Sin(phase) * 3f : 0f), new Vector3(0f, bob, 0f));

            // Lost: the head keeps swivelling, faster while stopped and squinting at the map.
            float look = walking ? Mathf.Sin(t * 0.9f) * 18f : Mathf.Sin(t * 2.4f) * 28f;
            head.Pose(new Vector3(0f, look, Mathf.Sin(t * 1.3f) * 6f));

            mark.Pose(new Vector3(0f, t * 90f, 0f), new Vector3(0f, Mathf.Sin(t * 3f) * 0.05f, 0f));

            AnimateMap();
        }

        void AnimateMap()
        {
            if (map.t == null) return;
            if (mapT < 0f) { map.Pose(new Vector3(0f, 0f, Mathf.Sin(Time.time * 1.7f) * 3f)); return; }

            mapT += Time.deltaTime;
            if (mapT <= mapFlightDuration)
            {
                float k = mapT / mapFlightDuration;
                // Up and back over their head, tumbling.
                var offset = new Vector3(0.3f * k, 1.6f * k - 1.4f * k * k, -0.9f * k);
                map.Pose(new Vector3(-540f * k, 120f * k, 200f * k), offset);
                map.t.gameObject.SetActive(true);
            }
            else if (mapT <= mapFlightDuration + mapRespawnSeconds)
            {
                map.t.gameObject.SetActive(false);
            }
            else
            {
                map.Pose(Vector3.zero);
                map.t.gameObject.SetActive(true);
                mapT = -1f;
            }
        }
    }
}
