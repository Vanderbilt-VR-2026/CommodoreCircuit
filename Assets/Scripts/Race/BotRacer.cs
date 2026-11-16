using System;
using System.Collections.Generic;
using CommodoreCircuit.Interactables;
using Fusion;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>
    /// How the bots drive. Lives on both race sessions, so it can be tuned in the Inspector.
    /// </summary>
    [Serializable]
    public class BotSettings
    {
        public const int MaxBots = 7;

        [Tooltip("Racers on the grid, players plus bots. Bots fill the spots players don't. 0 = no bots.")]
        [Range(0, 8)] public int fillTo = 4;

        [Header("Speed")]
        [Tooltip("Match the scooter's Max Speed (ScooterController > Speed).")]
        public float topSpeed = 14f;
        [Tooltip("The fastest bot drives at this fraction of top speed...")]
        [Range(0.5f, 1.2f)] public float fastestPace = 0.97f;
        [Tooltip("...and the slowest at this. Bots in between are spread evenly.")]
        [Range(0.5f, 1.2f)] public float slowestPace = 0.87f;
        public float acceleration = 14f;
        public float braking = 22f;

        [Header("Steering")]
        [Tooltip("Degrees per second at full speed. The scooter's own Turn Rate is 100.")]
        public float turnRate = 90f;
        [Tooltip("How far ahead along the road the bot aims (m)...")]
        public float lookahead = 5f;
        [Tooltip("...plus this many metres per m/s of speed. Bigger = smoother, but cuts corners wider.")]
        public float lookaheadPerSpeed = 0.35f;
        [Tooltip("How far ahead the bot looks for bends to slow down for (m).")]
        public float cornerScan = 18f;
        [Tooltip("Fraction of its pace a bot keeps through a 90 degree bend.")]
        [Range(0.2f, 1f)] public float cornerSpeed = 0.6f;
        [Tooltip("How far toward the inside of a bend the bot drives. 0 = centerline, 1 = right on the inside edge.")]
        [Range(0f, 1f)] public float cutCorners = 0.6f;
        [Tooltip("Gap kept from the road edge (m).")]
        public float edgeMargin = 1.2f;
        [Tooltip("Slow drift across the road, so bots don't look like they're on rails.")]
        [Range(0f, 1f)] public float wander = 0.25f;

        [Header("Drifting")]
        [Tooltip("Chance a bot drifts through a bend that's sharp enough.")]
        [Range(0f, 1f)] public float driftChance = 0.5f;
        [Tooltip("Bends at least this sharp (degrees within Corner Scan) can be drifted.")]
        public float driftMinBend = 35f;
        [Tooltip("How far the nose swings into the bend while drifting (degrees). Looks only.")]
        public float driftSlideAngle = 25f;

        [Header("Power-ups and shortcuts")]
        [Tooltip("Chance a bot steers for a power-up it sees ahead. Rolled per power-up, per lap.")]
        [Range(0f, 1f)] public float pickupChance = 0.6f;
        [Tooltip("How far ahead a bot notices power-ups (m).")]
        public float pickupRange = 25f;
        [Tooltip("Chance a bot takes a shortcut (Rand), rolled each lap.")]
        [Range(0f, 1f)] public float shortcutChance = 0.35f;
        [Tooltip("Fraction of its pace a bot keeps on the shortcut.")]
        [Range(0.3f, 1f)] public float shortcutSpeed = 0.85f;

        [Header("Rubber band")]
        [Tooltip("Bots far ahead of every player ease off; bots far behind every player speed up.")]
        public bool rubberBand = true;
        [Tooltip("Gap (m) at which the full slowdown or speed-up applies.")]
        public float rubberBandRange = 120f;
        [Range(0f, 0.5f)] public float rubberBandSlow = 0.15f;
        [Range(0f, 0.5f)] public float rubberBandBoost = 0.12f;
    }

    /// <summary>Everything another client needs to draw a bot and rank it. NetworkRaceSession keeps one per bot.</summary>
    public struct BotState : INetworkStruct
    {
        public Vector3 Position;
        public float Yaw;
        public float Speed;
        /// <summary>Drift: how far the nose is swung into the bend (degrees, + = right).</summary>
        public float Slip;
        public float Distance;
        public float LapStart;
        public float FinishTime;
        public float BestLap;
        public int CompletedLaps;
        /// <summary>Counts hazard hits. Other clients spin the bot out when it goes up.</summary>
        public int Hits;
        public float SpinDirection;
        /// <summary>Seconds into the current drift, 0 when not drifting.</summary>
        public float DriftCharge;
        /// <summary>See <see cref="BotRacer.BoostKind"/>.</summary>
        public int BoostKind;
        public NetworkBool Finished;
    }

    /// <summary>
    /// A computer racer. No ScooterController: it follows the RaceTrack centerline by aiming at
    /// a point a little way ahead (pure pursuit), cuts toward the inside of bends, slows for
    /// tight ones, and snaps to the ground with a raycast. On top of that it:
    ///   - steers for power-ups ahead (sometimes), and gets the boost or shield from them,
    ///   - hits hazards like a player: slowed, knocked sideways, spins out,
    ///   - wakes up the squirrels, pipes and frisbees (they look for RacerEffects nearby),
    ///   - takes the Rand shortcut on some laps,
    ///   - drifts through some sharp bends and gets a mini or super boost out of it.
    /// <see cref="BotRacerEffects"/> shows the sparks, boost flame and shield bubble.
    /// Its collider is a trigger, so players drive straight through bots.
    ///
    /// Solo, LocalRaceSession creates the bots and they drive themselves. Online, every client
    /// has a copy of each bot: on the master it drives (<see cref="Driving"/>) and
    /// NetworkRaceSession sends its <see cref="BotState"/>; everyone else follows that state.
    /// The copies keep their trigger, so every client sees the squirrel tumble and the
    /// power-up vanish; only the master's copy feels the effects.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BotRacer : MonoBehaviour
    {
        private static readonly Color[] Palette =
        {
            new Color(0.95f, 0.45f, 0.15f),
            new Color(0.2f, 0.75f, 0.7f),
            new Color(0.6f, 0.35f, 0.9f),
            new Color(0.95f, 0.4f, 0.65f),
            new Color(0.55f, 0.8f, 0.25f),
            new Color(0.3f, 0.5f, 0.95f),
            new Color(0.85f, 0.85f, 0.85f),
        };

        // Same numbers as the scooter's drift boost tiers.
        private const float MiniDrift = 0.7f, SuperDrift = 1.7f;
        private const float MiniBonus = 2.5f, SuperBonus = 3.5f;
        private const float MiniBoostTime = 0.4f, SuperBoostTime = 0.8f;
        private const float SpinSeconds = 0.8f;
        private const float MaxKnockback = 5f, KnockbackDecay = 12f, HitDeceleration = 25f;

        public static string NameFor(int index) => $"Bot {index + 1}";
        public static Color ColorFor(int index) => Palette[index % Palette.Length];

        public string DisplayName => NameFor(index);
        public Color Color => ColorFor(index);
        /// <summary>True: this client moves the bot. False: it follows <see cref="SetNetworkState"/>.</summary>
        public bool Driving { get; set; }

        // ---- Race numbers, same meaning as on RaceProgress ----
        public float RaceDistance { get; private set; }
        public int CompletedLaps { get; private set; }
        public bool Finished { get; private set; }
        public float FinishTime { get; private set; }
        public float BestLapTime { get; private set; }

        /// <summary>Seconds into the current drift, 0 when not drifting. Sets the spark color.</summary>
        public float DriftCharge => Driving ? (driftDirection != 0 ? driftTime : 0f) : netDriftCharge;
        /// <summary>0 none, 1 mini drift boost, 2 super drift boost, 3 speed cookie.</summary>
        public int BoostKind => Driving ? CurrentBoostKind() : netBoostKind;
        public const float MiniDriftCharge = MiniDrift, SuperDriftCharge = SuperDrift;

        private BotSettings settings;
        private int index;
        private float pace;
        private float laneBias;
        private float noiseSeed;
        private RacerEffects effects;

        private RaceTrack track;
        private int hint = -1;
        private float lastLapDistance = float.NaN;
        private float lapStart;

        // The bot's own pose, kept apart from the transform, which also carries lean, slip and spin.
        private Vector3 position;
        private float yaw;
        private float speed;
        private bool paused;
        private float noGroundTimer;
        private Vector3 knockback;

        // Drifting and its boost.
        private int driftDirection;
        private float driftTime;
        private bool cornerRolled;
        private float slip;
        private float boostTimer, boostBonus;

        // Spin-out after a hazard hit.
        private int hits;
        private float spinStart = -1f, spinDirection = 1f;

        // Shortcut: decided once per lap, then followed point by point.
        private Route route;
        private Route decidedRoute;
        private int decidedLap = -1;
        private bool takeShortcut;

        private float lean, leanVelocity, lastYaw;

        // Latest network state, for a bot this client doesn't drive.
        private bool hasNet;
        private Vector3 netPosition;
        private float netYaw, netSpeed, netSlip, netTime;
        private float netDriftCharge;
        private int netBoostKind;

        public static BotRacer Create(int index, int botCount, BotSettings settings)
        {
            var go = new GameObject(NameFor(index));
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // Same box as the scooter's body. A trigger, so it never blocks anyone.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.7f, 0f);
            box.size = new Vector3(0.35f, 0.6f, 1.4f);

            var bot = go.AddComponent<BotRacer>();
            bot.settings = settings;
            bot.index = index;
            // Bot 1 is the fastest.
            float t = botCount > 1 ? index / (float)(botCount - 1) : 0.5f;
            bot.pace = Mathf.Lerp(settings.fastestPace, settings.slowestPace, t);
            bot.laneBias = (index % 3 - 1) * 0.3f;
            bot.noiseSeed = index * 17.31f;
            bot.effects = go.AddComponent<RacerEffects>();
            bot.effects.haptics = false;
            bot.effects.onHit.AddListener(bot.OnHazardHit);
            go.AddComponent<RemoteRacerVisual>().Init(null, bot.Color);
            go.AddComponent<BotRacerEffects>();
            return bot;
        }

        /// <summary>A bot sitting on a grid slot, ready for the countdown.</summary>
        public static BotState StartState(Pose pose, RaceTrack track)
        {
            float d = track.Project(pose.position).Distance;
            return new BotState
            {
                Position = pose.position,
                Yaw = pose.rotation.eulerAngles.y,
                Distance = d > track.LapLength * 0.5f ? d - track.LapLength : d,
            };
        }

        public BotState Save() => new BotState
        {
            Position = position,
            Yaw = yaw,
            Speed = paused ? 0f : speed,
            Slip = slip,
            Distance = RaceDistance,
            LapStart = lapStart,
            FinishTime = FinishTime,
            BestLap = BestLapTime,
            CompletedLaps = CompletedLaps,
            Hits = hits,
            SpinDirection = spinDirection,
            DriftCharge = DriftCharge,
            BoostKind = BoostKind,
            Finished = Finished,
        };

        private int CurrentBoostKind()
        {
            if (boostTimer > 0f) return boostBonus >= SuperBonus ? 2 : 1;
            return effects && effects.IsBoosting ? 3 : 0;
        }

        /// <summary>Take over from <paramref name="s"/>: on creation, or when this client becomes the master.</summary>
        public void Load(BotState s)
        {
            position = s.Position;
            yaw = lastYaw = s.Yaw;
            speed = s.Speed;
            slip = s.Slip;
            lapStart = s.LapStart;
            hits = s.Hits;
            CopyNumbers(s);
            hint = -1;
            lastLapDistance = float.NaN;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        public void SetNetworkState(BotState s)
        {
            if (!hasNet)
            {
                Load(s);
                hasNet = true;
            }
            if (s.Position != netPosition || s.Yaw != netYaw) netTime = Time.time;
            netPosition = s.Position;
            netYaw = s.Yaw;
            netSpeed = s.Speed;
            netSlip = s.Slip;
            netDriftCharge = s.DriftCharge;
            netBoostKind = s.BoostKind;
            if (s.Hits != hits)
            {
                hits = s.Hits;
                spinDirection = s.SpinDirection;
                spinStart = Time.time;
            }
            CopyNumbers(s);
        }

        private void CopyNumbers(BotState s)
        {
            RaceDistance = s.Distance;
            CompletedLaps = s.CompletedLaps;
            Finished = s.Finished;
            FinishTime = s.FinishTime;
            BestLapTime = s.BestLap;
        }

        private void OnDestroy()
        {
            if (effects) effects.onHit.RemoveListener(OnHazardHit);
        }

        private void Update()
        {
            if (track == null) track = RaceTrack.Instance;
            if (Driving) Drive();
            else Follow();
            ApplyPose();
        }

        private void Drive()
        {
            var session = RaceSession.Current;
            if (track == null || !track.IsReady || session == null) return;
            paused = IsRacePaused();
            if (paused) return;

            float dt = Time.deltaTime;
            RacePhase phase = session.Phase;
            bool go = phase == RacePhase.Racing && !Finished;

            var here = track.Project(position, hint);
            if (route == null) hint = here.Segment;
            float bend = SharpestBend(here.Distance);
            float ahead = settings.lookahead + speed * settings.lookaheadPerSpeed;

            if (go) MaybeEnterShortcut(here.Distance, ahead);
            bool offRoad = route == null && Mathf.Abs(here.Lateral) > here.HalfWidth;
            if (offRoad) ahead = settings.lookahead;
            UpdateDrift(go && route == null, bend, speed, dt);

            Vector3 aim = route != null ? AimOnShortcut(ahead) : AimOnRoad(here, bend, ahead, offRoad);
            Vector3 toAim = aim - position;
            float wantYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
            float turnRate = settings.turnRate * (driftDirection != 0 ? 1.15f : 1f);
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turnRate * Mathf.Clamp01(speed / 4f) * dt);

            speed = Mathf.MoveTowards(speed, TargetSpeed(go, bend, offRoad, out float change), change * dt);

            Vector3 step = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * speed + knockback;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, KnockbackDecay * dt);
            position += step * dt;
            if (TryGroundY(position, 1.5f, 4f, out float groundY))
            {
                position.y = groundY;
                noGroundTimer = 0f;
            }
            else
            {
                noGroundTimer += dt;
            }
            if (noGroundTimer > 1.5f || position.y < -15f) Respawn(here.Distance);

            // Progress the way RaceProgress counts it, so a shortcut counts properly.
            var progress = track.ProjectWithShortcuts(position, route == null ? hint : -1);
            TrackDistance(progress.Distance, phase);
            if (go) CheckLaps(session);
        }

        // Aim at a point ahead on the road: toward the inside of the bend, toward a power-up
        // if it fancies one, plus a little wander.
        private Vector3 AimOnRoad(RaceTrack.TrackPoint here, float bend, float ahead, bool offRoad)
        {
            float aimDistance = here.Distance + ahead;
            Vector3 aim = track.PositionAt(aimDistance);
            if (offRoad) return aim;

            if (TryFindPickup(here.Distance, out PowerUp pickup, out float pickupAhead, out float pickupLateral))
            {
                // Close enough: drive right at it. Otherwise line up with it.
                if (pickupAhead <= ahead) return pickup.transform.position;
                float limit = Mathf.Max(0f, here.HalfWidth - 0.6f);
                return aim + Vector3.Cross(Vector3.up, Direction(aimDistance)) * Mathf.Clamp(pickupLateral, -limit, limit);
            }

            Vector3 right = Vector3.Cross(Vector3.up, Direction(aimDistance));
            float inside = Mathf.Clamp(bend / 45f, -1f, 1f) * settings.cutCorners;
            float wander = (Mathf.PerlinNoise(noiseSeed, Time.time * 0.2f) * 2f - 1f) * settings.wander;
            float room = Mathf.Max(0f, here.HalfWidth - settings.edgeMargin);
            return aim + right * Mathf.Clamp(inside + laneBias + wander, -1f, 1f) * room;
        }

        private float TargetSpeed(bool go, float bend, bool offRoad, out float change)
        {
            change = settings.acceleration;
            if (!go)
            {
                change = settings.braking;
                return 0f;
            }

            float corner = Mathf.Lerp(1f, settings.cornerSpeed, Mathf.Clamp01(Mathf.Abs(bend) / 90f));
            // Drifting carries more speed through the bend.
            if (driftDirection != 0) corner = Mathf.Lerp(corner, 1f, 0.4f);
            if (route != null) corner = settings.shortcutSpeed;
            float target = settings.topSpeed * pace * corner * RubberBand();
            if (offRoad) target *= 0.6f;

            if (boostTimer > 0f)
            {
                boostTimer -= Time.deltaTime;
                target = Mathf.Max(target, settings.topSpeed * pace + boostBonus);
                change = Mathf.Max(change, 30f);
            }

            // Hazard slowdown and speed cookies, same rule as the scooter.
            float effect = effects ? effects.SpeedMultiplier : 1f;
            if (effect < 0.999f)
            {
                target = Mathf.Min(target, settings.topSpeed * effect);
                if (speed > target) change = HitDeceleration;
            }
            else if (effect > 1.001f)
            {
                target = Mathf.Max(target, settings.topSpeed * effect);
                change = Mathf.Max(change, 30f);
            }

            // Slowing down: hard after a hit, gently easing off a boost, otherwise braking.
            if (target < speed)
                change = effect < 0.999f ? HitDeceleration
                    : speed > settings.topSpeed * pace ? 6f : settings.braking;
            return target;
        }

        // ---- Drifting ----

        // Each sharp bend gets one roll of the dice. A drift held long enough pays out a boost.
        private void UpdateDrift(bool allowed, float bend, float currentSpeed, float dt)
        {
            float sharp = Mathf.Abs(bend);
            if (sharp < settings.driftMinBend * 0.5f) cornerRolled = false;

            if (driftDirection == 0)
            {
                if (allowed && !cornerRolled && sharp >= settings.driftMinBend && currentSpeed > 8f)
                {
                    cornerRolled = true;
                    if (UnityEngine.Random.value < settings.driftChance)
                    {
                        driftDirection = bend > 0f ? 1 : -1;
                        driftTime = 0f;
                    }
                }
            }
            else
            {
                driftTime += dt;
                if (!allowed || sharp < 12f || driftTime > 3f || currentSpeed < 5f) EndDrift(allowed);
            }

            float targetSlip = driftDirection * settings.driftSlideAngle;
            slip = Mathf.MoveTowards(slip, targetSlip, 90f * dt);
        }

        private void EndDrift(bool payOut)
        {
            if (payOut && driftTime >= SuperDrift) { boostBonus = SuperBonus; boostTimer = SuperBoostTime; }
            else if (payOut && driftTime >= MiniDrift) { boostBonus = MiniBonus; boostTimer = MiniBoostTime; }
            driftDirection = 0;
            driftTime = 0f;
        }

        // ---- Hazards ----

        // Like the scooter: lose the drift and boost, get pushed sideways off the hazard, spin out.
        // The slowdown itself comes from RacerEffects.SpeedMultiplier.
        private void OnHazardHit(HazardHit hit)
        {
            if (!Driving) return;   // other clients' copies spin when Hits arrives
            EndDrift(false);
            boostTimer = 0f;

            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 push = hit.knockback;
            push.y = 0f;
            float side = Vector3.Dot(push, right);
            if (Mathf.Abs(side) < 0.1f * push.magnitude && hit.source != null)
                side = Vector3.Dot(position - hit.source.transform.position, right);
            float strength = Mathf.Min(push.magnitude, MaxKnockback);
            knockback = right * (side < 0f ? -1f : 1f) * strength;

            hits++;
            spinDirection = side < 0f ? 1f : -1f;
            spinStart = Time.time;
        }

        // ---- Power-ups ----

        private struct Pickup
        {
            public PowerUp PowerUp;
            public float Distance;
            public float Lateral;
        }

        private static readonly List<Pickup> Pickups = new List<Pickup>();
        private static RaceTrack pickupsFor;

        // The nearest available power-up ahead that this bot has decided it wants.
        private bool TryFindPickup(float lapDistance, out PowerUp best, out float bestAhead, out float lateral)
        {
            CachePickups();
            best = null;
            bestAhead = float.MaxValue;
            lateral = 0f;
            for (int i = 0; i < Pickups.Count; i++)
            {
                Pickup p = Pickups[i];
                if (!p.PowerUp || !p.PowerUp.Available) continue;
                float ahead = Mathf.Repeat(p.Distance - lapDistance, track.LapLength);
                if (ahead < 1f || ahead > settings.pickupRange || ahead >= bestAhead) continue;
                if (Roll(index, i, CompletedLaps) >= settings.pickupChance) continue;
                best = p.PowerUp;
                bestAhead = ahead;
                lateral = p.Lateral;
            }
            return best != null;
        }

        // Where each power-up on the road sits along the lap. Worked out once per track.
        private void CachePickups()
        {
            if (pickupsFor == track) return;
            pickupsFor = track;
            Pickups.Clear();
            foreach (var p in FindObjectsByType<PowerUp>(FindObjectsSortMode.None))
            {
                var at = track.Project(p.transform.position);
                if (Mathf.Abs(at.Lateral) > at.HalfWidth + 1f) continue;   // off the road, ignore
                Pickups.Add(new Pickup { PowerUp = p, Distance = at.Distance, Lateral = at.Lateral });
            }
        }

        // A fixed 0..1 roll per bot, power-up and lap, so the bot doesn't change its mind every frame.
        private static float Roll(int a, int b, int c)
            => Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f + c * 37.719f) * 43758.5453f, 1f);

        // ---- Shortcuts ----

        private class Route
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public float[] Cumulative;
            public float Length, Entry, Exit;
        }

        private static readonly List<Route> Routes = new List<Route>();
        private static RaceTrack routesFor;

        private void CacheRoutes()
        {
            if (routesFor == track) return;
            routesFor = track;
            Routes.Clear();
            foreach (var shortcut in TrackShortcut.All)
            {
                var r = new Route();
                if (!shortcut.TryGetRoute(track, r.Points, out r.Entry, out r.Exit)) continue;
                r.Cumulative = new float[r.Points.Count];
                for (int i = 1; i < r.Points.Count; i++)
                    r.Cumulative[i] = r.Cumulative[i - 1] + Flat(r.Points[i] - r.Points[i - 1]).magnitude;
                r.Length = Mathf.Max(0.01f, r.Cumulative[r.Points.Count - 1]);
                Routes.Add(r);
            }
        }

        // Coming up on a shortcut: roll once this lap, then turn in when it's within reach.
        private void MaybeEnterShortcut(float lapDistance, float ahead)
        {
            if (route != null) return;
            CacheRoutes();
            foreach (var r in Routes)
            {
                float toEntry = Mathf.Repeat(r.Entry - lapDistance, track.LapLength);
                if (toEntry > ahead + 25f) continue;
                if (decidedRoute != r || decidedLap != CompletedLaps)
                {
                    decidedRoute = r;
                    decidedLap = CompletedLaps;
                    takeShortcut = UnityEngine.Random.value < settings.shortcutChance;
                }
                if (takeShortcut && toEntry <= ahead)
                {
                    route = r;
                    EndDrift(true);
                }
                return;
            }
        }

        // Follow the shortcut's path; past its end, aim back onto the lap where it rejoins.
        private Vector3 AimOnShortcut(float ahead)
        {
            float along = AlongRoute(route, position, out float offSq);
            if (along >= route.Length - 1f || offSq > 12f * 12f)
            {
                // Done (or knocked well off it): back to the road.
                Route done = route;
                route = null;
                hint = -1;
                return track.PositionAt(done.Exit + ahead);
            }
            float target = along + ahead;
            return target <= route.Length
                ? RoutePoint(route, target)
                : track.PositionAt(route.Exit + (target - route.Length));
        }

        private static float AlongRoute(Route r, Vector3 p, out float distSq)
        {
            float best = 0f;
            distSq = float.MaxValue;
            for (int i = 0; i + 1 < r.Points.Count; i++)
            {
                Vector3 a = r.Points[i], ab = Flat(r.Points[i + 1] - a);
                float lenSq = ab.sqrMagnitude;
                float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / lenSq) : 0f;
                float d = Flat(a + ab * t - p).sqrMagnitude;
                if (d < distSq)
                {
                    distSq = d;
                    best = r.Cumulative[i] + t * (r.Cumulative[i + 1] - r.Cumulative[i]);
                }
            }
            return best;
        }

        private static Vector3 RoutePoint(Route r, float along)
        {
            for (int i = 0; i + 1 < r.Points.Count; i++)
            {
                if (along > r.Cumulative[i + 1]) continue;
                float seg = r.Cumulative[i + 1] - r.Cumulative[i];
                return Vector3.Lerp(r.Points[i], r.Points[i + 1], seg > 0f ? (along - r.Cumulative[i]) / seg : 0f);
            }
            return r.Points[r.Points.Count - 1];
        }

        // ---- Following the network ----

        // Head for the latest network pose, guessing ahead a little so it doesn't stutter at tick rate.
        private void Follow()
        {
            if (!hasNet) return;
            float age = Mathf.Min(Time.time - netTime, 0.2f);
            Vector3 predicted = netPosition + Quaternion.Euler(0f, netYaw, 0f) * Vector3.forward * (netSpeed * age);
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            position = (predicted - position).sqrMagnitude > 25f ? predicted : Vector3.Lerp(position, predicted, k);
            yaw = Mathf.LerpAngle(yaw, netYaw, k);
            slip = Mathf.Lerp(slip, netSlip, k);
            speed = netSpeed;
        }

        // Lean into turns like the real scooter, swing the nose in while drifting, and spin out after a hit.
        private void ApplyPose()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            bool drifting = Mathf.Abs(slip) > 1f;
            float leanAmount = drifting ? 18f : 12f;
            float leanTarget = -Mathf.Clamp(yawRate / 90f, -1f, 1f) * leanAmount * Mathf.Clamp01(speed / 6f);
            if (drifting) leanTarget = -Mathf.Sign(slip) * leanAmount * Mathf.Clamp01(speed / 6f);
            lean = Mathf.SmoothDamp(lean, leanTarget, ref leanVelocity, 0.2f);

            float spin = 0f;
            if (spinStart >= 0f)
            {
                float t = (Time.time - spinStart) / SpinSeconds;
                if (t >= 1f) spinStart = -1f;
                else spin = spinDirection * 360f * (1f - (1f - t) * (1f - t) * (1f - t));
            }

            transform.SetPositionAndRotation(position,
                Quaternion.Euler(0f, yaw + slip + spin, 0f) * Quaternion.Euler(0f, 0f, lean));
        }

        // ---- Track helpers ----

        // Signed angle (+ = right) of the tightest bend within Corner Scan metres.
        private float SharpestBend(float distance)
        {
            Vector3 now = Direction(distance);
            float sharpest = 0f;
            for (int k = 1; k <= 3; k++)
            {
                float a = Vector3.SignedAngle(now, Direction(distance + settings.cornerScan * k / 3f), Vector3.up);
                if (Mathf.Abs(a) > Mathf.Abs(sharpest)) sharpest = a;
            }
            return sharpest;
        }

        private Vector3 Direction(float distance)
        {
            Vector3 d = Flat(track.PositionAt(distance + 1.5f) - track.PositionAt(distance - 1.5f));
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private float RubberBand()
        {
            if (!settings.rubberBand || !HumanRange(out float last, out float first)) return 1f;
            float range = Mathf.Max(1f, settings.rubberBandRange);
            if (RaceDistance > first) return 1f - settings.rubberBandSlow * Mathf.Clamp01((RaceDistance - first) / range);
            if (RaceDistance < last) return 1f + settings.rubberBandBoost * Mathf.Clamp01((last - RaceDistance) / range);
            return 1f;
        }

        // Distance of the last and the leading human racer.
        private static bool HumanRange(out float last, out float first)
        {
            last = float.MaxValue;
            first = float.MinValue;
            if (RaceSession.HasNetworkSession)
            {
                foreach (var r in NetworkRacer.All)
                {
                    if (!r || !r.IsLive) continue;
                    last = Mathf.Min(last, r.Distance);
                    first = Mathf.Max(first, r.Distance);
                }
            }
            else if (RaceProgress.Local)
            {
                last = first = RaceProgress.Local.RaceDistance;
            }
            return last <= first;
        }

        private static bool IsRacePaused()
        {
            if (RaceSession.HasNetworkSession)
                return NetworkRaceSession.Instance && NetworkRaceSession.Instance.IsPaused;
            return RaceProgress.Local && RaceProgress.Local.IsPaused;
        }

        // Solo, the bots share the player's clock, which stops while the pause menu is open.
        private static float Clock(IRaceSession session)
            => !session.IsNetworked && RaceProgress.Local ? RaceProgress.Local.RaceTime : session.RaceTime;

        // Fell off the map or lost the ground: back onto the centerline where it left.
        private void Respawn(float distance)
        {
            position = track.PositionAt(distance);
            if (TryGroundY(position, 50f, 100f, out float groundY)) position.y = groundY;
            Vector3 f = Direction(distance);
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            speed = 0f;
            knockback = Vector3.zero;
            noGroundTimer = 0f;
            hint = -1;
            route = null;
            EndDrift(false);
        }

        private static bool TryGroundY(Vector3 at, float above, float range, out float groundY)
        {
            groundY = at.y;
            float closest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * above, Vector3.down, range, ~0, QueryTriggerInteraction.Ignore))
            {
                // Skip walls and anyone's scooter.
                if (hit.normal.y < 0.6f || hit.collider.GetComponentInParent<Scooter.ScooterController>()) continue;
                if (hit.distance < closest)
                {
                    closest = hit.distance;
                    groundY = hit.point.y;
                }
            }
            return closest < float.MaxValue;
        }

        // Same bookkeeping as RaceProgress: one continuous distance across laps.
        private void TrackDistance(float lapDistance, RacePhase phase)
        {
            float L = track.LapLength;
            if (phase <= RacePhase.Countdown)
            {
                RaceDistance = lapDistance > L * 0.5f ? lapDistance - L : lapDistance;
                lastLapDistance = lapDistance;
                return;
            }
            if (float.IsNaN(lastLapDistance))
            {
                // Just took over this bot mid-race: keep the distance we were handed.
                lastLapDistance = lapDistance;
                return;
            }

            float delta = lapDistance - lastLapDistance;
            if (delta > L * 0.5f) delta -= L;
            else if (delta < -L * 0.5f) delta += L;
            lastLapDistance = lapDistance;
            if (Mathf.Abs(delta) > 30f) return;
            RaceDistance += delta;
        }

        private void CheckLaps(IRaceSession session)
        {
            int laps = Mathf.Max(1, session.Laps);
            int reached = Mathf.FloorToInt(RaceDistance / track.LapLength);
            while (reached > CompletedLaps && !Finished)
            {
                float now = Clock(session);
                float lapTime = now - lapStart;
                lapStart = now;
                if (BestLapTime <= 0f || lapTime < BestLapTime) BestLapTime = lapTime;
                CompletedLaps++;
                if (CompletedLaps >= laps)
                {
                    Finished = true;
                    FinishTime = now;
                }
            }
        }
    }
}
