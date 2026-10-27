using System.Collections.Generic;
using UnityEngine;

namespace CommodoreCircuit.Race
{
    /// <summary>Where the race is. Stored as a byte so it can be a [Networked] value.</summary>
    public enum RacePhase : byte
    {
        /// <summary>Scene loaded, waiting for every racer to arrive.</summary>
        Waiting,
        /// <summary>"Get ready" then 3-2-1. Scooters can't accelerate yet.</summary>
        Countdown,
        /// <summary>GO! The clock is running.</summary>
        Racing,
        /// <summary>Everyone finished (or the finish grace ran out). Standings are final.</summary>
        Results,
    }

    /// <summary>One row of the standings, already sorted and numbered.</summary>
    public struct RaceStanding
    {
        public int Position;          // 1-based
        public string DisplayName;
        public Color Color;
        public bool IsLocal;
        public bool Finished;
        public float FinishTime;      // seconds from GO, valid when Finished
        public float BestLap;         // seconds, 0 = no lap completed yet
        public int CompletedLaps;
        public float Distance;        // metres since the start line, across laps
    }

    /// <summary>
    /// What the HUD and the racer need from a race, whether it runs solo
    /// (<see cref="LocalRaceSession"/>) or over Photon Fusion (<see cref="NetworkRaceSession"/>).
    /// </summary>
    public interface IRaceSession
    {
        RacePhase Phase { get; }
        int Laps { get; }
        /// <summary>Length of the 3-2-1 part of the countdown, in seconds.</summary>
        float CountdownSeconds { get; }
        /// <summary>Seconds until GO. Positive during the countdown, 0 or less after.</summary>
        float TimeUntilGo { get; }
        /// <summary>Seconds since GO (0 before it).</summary>
        float RaceTime { get; }
        /// <summary>True when standings include other players over the network.</summary>
        bool IsNetworked { get; }
        /// <summary>Seconds left before the game leaves the results screen, or -1 if it doesn't.</summary>
        float ResultsTimeRemaining { get; }
        /// <summary>Clears <paramref name="into"/> and fills it with the current standings, 1st first.</summary>
        void GetStandings(List<RaceStanding> into);
        /// <summary>Solo only: run the race again. Ignored over the network.</summary>
        void RequestRestart();
    }

    /// <summary>
    /// Finds the race that is running in this scene. The networked session outranks the
    /// solo one, so a scene can contain both: offline the solo session runs, and once Fusion
    /// spawns the network session it takes over.
    /// </summary>
    public static class RaceSession
    {
        public const int SoloPriority = 0;
        public const int NetworkPriority = 10;

        private static IRaceSession current;
        private static int currentPriority = int.MinValue;

        public static IRaceSession Current => current;

        public static void Register(IRaceSession session, int priority)
        {
            if (current == null || priority >= currentPriority)
            {
                current = session;
                currentPriority = priority;
            }
        }

        public static void Unregister(IRaceSession session)
        {
            if (current != session) return;
            current = null;
            currentPriority = int.MinValue;
        }

        public static bool HasNetworkSession => current != null && currentPriority >= NetworkPriority;

        /// <summary>
        /// Finished racers first by finish time, then everyone else by distance covered.
        /// Assigns Position. Shared by both session types so they rank identically.
        /// </summary>
        public static void Rank(List<RaceStanding> standings)
        {
            standings.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                return b.Distance.CompareTo(a.Distance);
            });
            for (int i = 0; i < standings.Count; i++)
            {
                var s = standings[i];
                s.Position = i + 1;
                standings[i] = s;
            }
        }
    }

    public static class RaceFormat
    {
        /// <summary>83.456 -> "01:23.45" (minutes:seconds.hundredths, like the HUD numerals sheet).</summary>
        public static string Time(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds)) seconds = 0f;
            int hundredths = Mathf.FloorToInt(seconds * 100f);
            int minutes = hundredths / 6000;
            int secs = hundredths / 100 % 60;
            int frac = hundredths % 100;
            return $"{minutes:00}:{secs:00}.{frac:00}";
        }

        public const string NoTime = "--:--.--";

        /// <summary>1 -> "1ST", 2 -> "2ND", 11 -> "11TH".</summary>
        public static string Ordinal(int n)
        {
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "TH"
                : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" };
            return n + suffix;
        }
    }

    /// <summary>Short controller rumbles. Never shake the camera in VR.</summary>
    public static class RaceHaptics
    {
        private static readonly List<UnityEngine.XR.InputDevice> Devices = new List<UnityEngine.XR.InputDevice>();

        public static void Pulse(float amplitude, float duration)
        {
            Devices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Controller, Devices);
            foreach (var device in Devices)
                if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }
    }

    /// <summary>
    /// Tiny procedural sounds so the race works before the team has real audio.
    /// Assign real clips on the views to replace them.
    /// </summary>
    public static class RaceTones
    {
        private static readonly Dictionary<int, AudioClip> Cache = new Dictionary<int, AudioClip>();

        /// <param name="frequencies">Played one after another, each for <paramref name="noteSeconds"/>.</param>
        public static AudioClip Get(float noteSeconds, params float[] frequencies)
        {
            int key = Mathf.RoundToInt(noteSeconds * 1000f);
            foreach (float f in frequencies) key = key * 31 + Mathf.RoundToInt(f);
            if (Cache.TryGetValue(key, out var clip) && clip) return clip;

            const int rate = 44100;
            int perNote = Mathf.Max(1, Mathf.RoundToInt(noteSeconds * rate));
            var data = new float[perNote * frequencies.Length];
            for (int n = 0; n < frequencies.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = i / (float)rate;
                    float attack = Mathf.Clamp01(i / (rate * 0.005f));
                    float release = Mathf.Clamp01((perNote - i) / (rate * 0.06f));
                    // Square-ish arcade tone: sine plus a little third harmonic.
                    float w = 2f * Mathf.PI * frequencies[n] * t;
                    data[n * perNote + i] = (Mathf.Sin(w) + 0.25f * Mathf.Sin(3f * w)) * 0.35f * attack * release;
                }
            }
            clip = AudioClip.Create($"RaceTone_{key}", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            Cache[key] = clip;
            return clip;
        }
    }
}
