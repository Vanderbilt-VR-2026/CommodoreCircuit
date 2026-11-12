using CommodoreCircuit.Scooter;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// A bot's drift sparks, boost flame and Commodore Card shield bubble.
    ///
    /// Like <see cref="RemoteRacerVisual"/>, nothing is set up by hand: the first time this
    /// client's scooter is around, its DriftSparks and BoostFlame particle systems and its
    /// Shield Bubble are copied onto the bot, in the same spot relative to the scooter. So the
    /// bots always look like the player, and tweaking the player's effects tweaks theirs.
    /// A scooter without those (no Boost Effects / Shield Bubble added) just gives bots without them.
    ///
    ///   Sparks  - while the bot drifts, colored by charge like ScooterBoostEffects
    ///   Flame   - burst and stream while it has a drift boost (blue/orange) or a speed cookie (gold)
    ///   Shield  - the copied ScooterShieldBubble finds the bot's RacerEffects and runs itself
    /// </summary>
    [RequireComponent(typeof(BotRacer))]
    public class BotRacerEffects : MonoBehaviour
    {
        private const float SparkRate = 60f, SparkRateGainPerTier = 0.5f;
        private const float FlameRate = 90f;
        private const int FlameBurst = 30;

        private static readonly Color NoChargeColor = new Color(1f, 0.85f, 0.5f);
        private static readonly Color MiniColor = new Color(0.3f, 0.65f, 1f);
        private static readonly Color SuperColor = new Color(1f, 0.55f, 0.1f);
        private static readonly Color CookieColor = new Color(1f, 0.78f, 0.17f);

        private BotRacer bot;
        private ParticleSystem sparks, flame;
        private bool built;
        private float nextTry;
        private int shownSparkTier = -1;
        private int shownBoost;

        private void Awake() => bot = GetComponent<BotRacer>();

        private void LateUpdate()
        {
            if (!built)
            {
                // Our scooter may not exist for a moment while the scene finishes loading.
                if (Time.unscaledTime < nextTry) return;
                nextTry = Time.unscaledTime + 0.5f;
                built = TryBuild();
                if (!built) return;
            }
            UpdateSparks();
            UpdateFlame();
        }

        private bool TryBuild()
        {
            ScooterController source = RaceProgress.Local ? RaceProgress.Local.Scooter : null;
            if (!source) source = FindAnyObjectByType<ScooterController>();
            if (!source) return false;

            var boost = source.GetComponentInChildren<ScooterBoostEffects>(true);
            if (boost)
            {
                sparks = Copy(boost.DriftSparks, source.transform);
                flame = Copy(boost.BoostFlame, source.transform);
            }
            var shield = source.GetComponentInChildren<ScooterShieldBubble>(true);
            if (shield) Copy(shield.transform, source.transform);
            return true;
        }

        private void UpdateSparks()
        {
            if (!sparks) return;
            float charge = bot.DriftCharge;
            int tier = charge >= BotRacer.SuperDriftCharge ? 2 : charge >= BotRacer.MiniDriftCharge ? 1 : 0;
            SetRate(sparks, charge > 0f ? SparkRate * (1f + SparkRateGainPerTier * tier) : 0f);
            if (tier != shownSparkTier)
            {
                SetColor(sparks, tier == 2 ? SuperColor : tier == 1 ? MiniColor : NoChargeColor);
                shownSparkTier = tier;
            }
        }

        private void UpdateFlame()
        {
            if (!flame) return;
            int boost = bot.BoostKind;
            SetRate(flame, boost > 0 ? FlameRate : 0f);
            if (boost != shownBoost && boost > 0)
            {
                SetColor(flame, boost == 3 ? CookieColor : boost == 2 ? SuperColor : MiniColor);
                flame.Emit(FlameBurst);
            }
            shownBoost = boost;
        }

        private ParticleSystem Copy(ParticleSystem original, Transform scooter)
        {
            if (!original) return null;
            var copy = Copy(original.transform, scooter).GetComponent<ParticleSystem>();
            SetRate(copy, 0f);
            if (!copy.isPlaying) copy.Play();
            return copy;
        }

        // Copy a part of the scooter onto the bot, posed the same relative to the scooter root.
        // It goes under an anchor matching the part's parent, so its own local transform (which
        // the shield bubble reads in Awake) stays exactly as on the scooter.
        private Transform Copy(Transform original, Transform scooter)
        {
            var anchor = new GameObject(original.name + " Anchor").transform;
            anchor.SetParent(transform, false);
            Matrix4x4 m = scooter.worldToLocalMatrix * original.parent.localToWorldMatrix;
            anchor.localPosition = m.GetColumn(3);
            anchor.localRotation = m.rotation;
            anchor.localScale = m.lossyScale;
            var copy = Instantiate(original.gameObject, anchor, false);
            copy.name = original.name;
            return copy.transform;
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private static void SetColor(ParticleSystem ps, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = color;
        }
    }
}
