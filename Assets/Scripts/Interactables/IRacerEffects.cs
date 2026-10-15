using UnityEngine;

namespace CommodoreCircuit.Interactables
{
    /// <summary>
    /// What hazards and power-ups call on a racer. Put a component that implements
    /// this (e.g. <see cref="RacerEffects"/>) on the player's scooter root, the same
    /// GameObject as (or a parent of) the scooter's collider.
    /// </summary>
    public interface IRacerEffects
    {
        bool IsShielded { get; }

        /// <returns>True if the hit landed, false if a shield absorbed it.</returns>
        bool ApplyHazard(HazardHit hit);

        void ApplyBoost(float speedMultiplier, float duration);

        void GrantShield(float duration);
    }

    [System.Serializable]
    public struct HazardHit
    {
        public float slowDuration;
        public float slowMultiplier;
        public Vector3 knockback;
        public Hazard source;
    }
}
