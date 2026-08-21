using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Helpers;
using BuddyCron.Objects;
using Reborn.Utilities;

namespace DefaultCombat.Helpers
{
    /// <summary>Confidence state for a target's health-loss estimate.</summary>
    public enum TimeToDieState
    {
        Unknown,
        Learning,
        Stable,
        Stalled
    }

    /// <summary>Smoothed remaining-lifetime estimate for one hostile unit.</summary>
    public sealed class TimeToDieEstimate
    {
        public static readonly TimeToDieEstimate Unknown =
            new TimeToDieEstimate(TimeToDieState.Unknown, double.PositiveInfinity, 0, 0, 0);

        public TimeToDieEstimate(TimeToDieState state, double seconds, double healthLossPerSecond,
            double sampleSpanSeconds, double observedDamagePercent)
        {
            State = state;
            Seconds = seconds;
            HealthLossPerSecond = healthLossPerSecond;
            SampleSpanSeconds = sampleSpanSeconds;
            ObservedDamagePercent = observedDamagePercent;
        }

        public TimeToDieState State { get; }
        public double Seconds { get; }
        public double HealthLossPerSecond { get; }
        public double SampleSpanSeconds { get; }
        public double ObservedDamagePercent { get; }
        public bool IsStable => State == TimeToDieState.Stable;
    }

    /// <summary>
    /// Tracks recent health samples for every engaged hostile. Each cast decision refreshes the
    /// selected unit from both the absolute and percentage health APIs, then combines a smoothed
    /// history with a short burst-damage window and a rank-aware live-health floor.
    /// </summary>
    public static class TimeToDie
    {
        private const double SampleIntervalSeconds = 0.25;
        private const double SampleWindowSeconds = 8.0;
        private const double RecentDamageWindowSeconds = 2.0;
        private const double BurstDamageWindowSeconds = 4.0;
        private const double MinimumBurstSpanSeconds = 0.5;
        private const double MinimumEstimateSpanSeconds = 1.25;
        private const double StableEstimateSpanSeconds = 3.5;
        private const double MinimumObservedDamagePercent = 1.0;
        private const double StableObservedDamagePercent = 5.0;
        private const double HighValueObservedDamagePercent = 12.0;
        private const float ForcedSampleDamagePercent = 0.25f;
        private const double StalledAfterSeconds = 2.75;
        private const double HistoryRetentionSeconds = 15.0;
        private const double DiagnosticRepeatSeconds = 5.0;
        private const float HealingResetPercent = 4.0f;
        private const double MaximumReportedSeconds = 300.0;

        private sealed class HealthSample
        {
            public DateTime TimestampUtc;
            public float HealthPercent;
        }

        private sealed class TargetHistory
        {
            public readonly List<HealthSample> Samples = new List<HealthSample>();
            public DateTime LastSeenUtc;
            public DateTime LastDamageUtc;
            public bool HasTakenDamage;
        }

        private sealed class DecisionLog
        {
            public DateTime LastUtc;
            public string StateToken = string.Empty;
        }

        private static readonly Dictionary<ulong, TargetHistory> s_histories =
            new Dictionary<ulong, TargetHistory>();

        private static readonly Dictionary<string, DecisionLog> s_decisionLogs =
            new Dictionary<string, DecisionLog>();

        /// <summary>Clears all samples during routine reload or shutdown.</summary>
        public static void Reset()
        {
            s_histories.Clear();
            s_decisionLogs.Clear();
        }

        /// <summary>Samples every engaged enemy plus the selected hostile target once per scan.</summary>
        public static void Update(IEnumerable<HeroCharacter> enemies)
        {
            var now = DateTime.UtcNow;
            var observed = new Dictionary<ulong, HeroCharacter>();

            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy == null || enemy.IsDead || !enemy.IsEngagedWithPlayer())
                        continue;
                    observed[enemy.NodeId] = enemy;
                }
            }

            var selected = Core.Player != null ? Core.Player.Target : null;
            if (selected != null && !selected.IsDead && selected.IsEffectivePvEHostile() &&
                selected.IsEngagedWithPlayer())
                observed[selected.NodeId] = selected;

            foreach (var enemy in observed.Values)
            {
                try
                {
                    Sample(enemy, now);
                }
                catch
                {
                    // GOM nodes can disappear between the target scan and the health read.
                }
            }

            var expired = s_histories
                .Where(pair => (now - pair.Value.LastSeenUtc).TotalSeconds > HistoryRetentionSeconds)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var id in expired)
                s_histories.Remove(id);
        }

        /// <summary>Returns the current estimate for <paramref name="target"/>.</summary>
        public static TimeToDieEstimate Estimate(HeroCharacter target)
        {
            if (target == null || target.IsDead || !s_histories.TryGetValue(target.NodeId, out var history))
                return TimeToDieEstimate.Unknown;

            var samples = history.Samples;
            if (samples.Count < 3)
                return new TimeToDieEstimate(TimeToDieState.Learning, double.PositiveInfinity, 0, 0, 0);

            var now = DateTime.UtcNow;
            var first = samples[0];
            var last = samples[samples.Count - 1];
            double span = (last.TimestampUtc - first.TimestampUtc).TotalSeconds;
            double totalObservedDamage = Math.Max(0, first.HealthPercent - last.HealthPercent);

            if (history.HasTakenDamage &&
                (now - history.LastDamageUtc).TotalSeconds >= StalledAfterSeconds &&
                last.HealthPercent < 99.5f)
            {
                return new TimeToDieEstimate(TimeToDieState.Stalled, double.PositiveInfinity, 0, span,
                    totalObservedDamage);
            }

            if (span < MinimumEstimateSpanSeconds)
                return new TimeToDieEstimate(TimeToDieState.Learning, double.PositiveInfinity, 0, span,
                    totalObservedDamage);

            // Weighted linear regression gives recent samples twice the influence of the oldest
            // sample, smoothing one large crit without making the estimate slow to react.
            double weightSum = 0;
            double weightedTime = 0;
            double weightedHealth = 0;
            foreach (var sample in samples)
            {
                double x = (sample.TimestampUtc - first.TimestampUtc).TotalSeconds;
                double weight = 1.0 + (x / span);
                weightSum += weight;
                weightedTime += weight * x;
                weightedHealth += weight * sample.HealthPercent;
            }

            double meanTime = weightedTime / weightSum;
            double meanHealth = weightedHealth / weightSum;
            double numerator = 0;
            double denominator = 0;
            foreach (var sample in samples)
            {
                double x = (sample.TimestampUtc - first.TimestampUtc).TotalSeconds;
                double weight = 1.0 + (x / span);
                double dx = x - meanTime;
                numerator += weight * dx * (sample.HealthPercent - meanHealth);
                denominator += weight * dx * dx;
            }

            double regressionLossPerSecond = 0;
            if (denominator > 0.0001)
            {
                double slope = numerator / denominator;
                if (slope < 0)
                    regressionLossPerSecond = -slope;
            }

            // The long regression is intentionally smooth, but that made it optimistic after a
            // sudden crit or group burst. Use the faster two-second rate whenever it is higher.
            var recentFirst = last;
            for (int i = samples.Count - 2; i >= 0; i--)
            {
                if ((last.TimestampUtc - samples[i].TimestampUtc).TotalSeconds > RecentDamageWindowSeconds)
                    break;
                recentFirst = samples[i];
            }

            double recentSpan = (last.TimestampUtc - recentFirst.TimestampUtc).TotalSeconds;
            double recentDamage = recentFirst.HealthPercent - last.HealthPercent;
            double recentLossPerSecond = recentSpan >= 0.10 && recentDamage >= ForcedSampleDamagePercent
                ? recentDamage / recentSpan
                : 0;

            double burstLossPerSecond = 0;
            for (int i = samples.Count - 2; i >= 0; i--)
            {
                double burstSpan = (last.TimestampUtc - samples[i].TimestampUtc).TotalSeconds;
                if (burstSpan > BurstDamageWindowSeconds)
                    break;
                if (burstSpan < MinimumBurstSpanSeconds)
                    continue;

                double burstDamage = samples[i].HealthPercent - last.HealthPercent;
                if (burstDamage >= ForcedSampleDamagePercent)
                    burstLossPerSecond = Math.Max(burstLossPerSecond, burstDamage / burstSpan);
            }

            double observedDamage = Math.Max(totalObservedDamage, recentDamage);
            double lossPerSecond = Math.Max(regressionLossPerSecond,
                Math.Max(recentLossPerSecond, burstLossPerSecond));
            if (lossPerSecond < 0.05 || observedDamage < MinimumObservedDamagePercent)
                return new TimeToDieEstimate(TimeToDieState.Learning, double.PositiveInfinity, 0, span,
                    observedDamage);

            double seconds = last.HealthPercent / lossPerSecond;
            seconds = Math.Max(0, Math.Min(MaximumReportedSeconds, seconds));
            var state = span >= StableEstimateSpanSeconds && observedDamage >= StableObservedDamagePercent
                ? TimeToDieState.Stable
                : TimeToDieState.Learning;

            return new TimeToDieEstimate(state, seconds, lossPerSecond, span, observedDamage);
        }

        /// <summary>
        /// True when the stable estimate exceeds <paramref name="minimumSeconds"/>. While samples
        /// are still learning, toughness and health provide a conservative leveling-safe fallback.
        /// A previously damaged target whose health has stalled never opens a long-payoff window.
        /// </summary>
        public static bool WillLiveFor(HeroCharacter target, double minimumSeconds)
        {
            TimeToDieEstimate estimate;
            float liveHealth;
            double healthFloor;
            string reason;
            return EvaluateWillLiveFor(target, minimumSeconds, out estimate, out liveHealth,
                out healthFloor, out reason);
        }

        /// <summary>
        /// Named overload used by high-value abilities. It records one throttled diagnostic when
        /// the allow/block reason changes, making field reports actionable without log spam.
        /// </summary>
        public static bool WillLiveFor(HeroCharacter target, double minimumSeconds, string abilityName)
        {
            TimeToDieEstimate estimate;
            float liveHealth;
            double healthFloor;
            string reason;
            bool allowed = EvaluateWillLiveFor(target, minimumSeconds, out estimate, out liveHealth,
                out healthFloor, out reason);
            LogDecision(target, abilityName, minimumSeconds, allowed, estimate, liveHealth, healthFloor, reason);
            return allowed;
        }

        public static bool HasUsefulCastsRemaining(HeroCharacter target, int minimumGlobalCooldowns,
            double durableMinimumSeconds, double standardMinimumSeconds, string abilityName)
        {
            bool eliteOrGreater = target != null &&
                                  (target.Toughness == cbtToughnessEnum.boss_1 ||
                                   target.BossOrGreater());
            double minimumSeconds = eliteOrGreater
                ? durableMinimumSeconds
                : standardMinimumSeconds;
            minimumSeconds = Math.Max(minimumSeconds, minimumGlobalCooldowns * 1.5);

            TimeToDieEstimate estimate;
            float liveHealth;
            double healthFloor;
            string reason;
            bool allowed = EvaluateWillLiveFor(target, minimumSeconds, out estimate, out liveHealth,
                out healthFloor, out reason);

            bool championOrGreater = target != null && target.BossOrGreater();
            if (allowed && !championOrGreater && estimate.State != TimeToDieState.Stable)
            {
                allowed = false;
                reason = "target-lifetime-unproven";
            }
            else if (allowed && !championOrGreater &&
                     estimate.ObservedDamagePercent < HighValueObservedDamagePercent)
            {
                allowed = false;
                reason = "insufficient-damage-history";
            }

            LogDecision(target, abilityName, minimumSeconds, allowed, estimate, liveHealth, healthFloor, reason);
            return allowed;
        }

        /// <summary>True when any engaged enemy gives a personal cooldown enough payoff time.</summary>
        public static bool PackWillLiveFor(IEnumerable<HeroCharacter> enemies, double minimumSeconds)
        {
            return enemies != null && enemies.Any(enemy =>
                enemy != null && enemy.IsEngagedWithPlayer() && WillLiveFor(enemy, minimumSeconds));
        }

        /// <summary>
        /// Returns the preferred target when it meets the payoff window, otherwise the most durable
        /// engaged target. Used for DoTs so a dying selected target does not waste the application.
        /// </summary>
        public static HeroCharacter BestSustainedTarget(IEnumerable<HeroCharacter> enemies,
            double minimumSeconds, HeroCharacter preferred)
        {
            if (preferred != null && WillLiveFor(preferred, minimumSeconds))
                return preferred;

            if (enemies == null)
                return null;

            return enemies
                .Where(enemy => enemy != null && enemy.IsEngagedWithPlayer() && !enemy.IsDead &&
                                enemy.DistanceSqr <= Distance.Ranged * Distance.Ranged &&
                                WillLiveFor(enemy, minimumSeconds))
                .OrderByDescending(enemy => Estimate(enemy).IsStable)
                .ThenByDescending(enemy => Estimate(enemy).Seconds)
                .ThenByDescending(enemy => enemy.BossOrGreater())
                .ThenByDescending(enemy => enemy.StrongOrGreater())
                .ThenByDescending(enemy => enemy.HealthPercent)
                .FirstOrDefault();
        }

        /// <summary>Counts enemies in an AoE cluster expected to survive the requested window.</summary>
        public static int CountClusterTargetsLivingFor(IEnumerable<HeroCharacter> enemies, Vector3 center,
            float radius, double minimumSeconds)
        {
            if (enemies == null || center == Vector3.Zero)
                return 0;

            float radiusSquared = radius * radius;
            return enemies.Count(enemy => enemy != null && enemy.IsEngagedWithPlayer() && !enemy.IsDead &&
                                          Vector3.DistanceSquared(enemy.Location, center) <= radiusSquared &&
                                          WillLiveFor(enemy, minimumSeconds));
        }

        private static void Sample(HeroCharacter target, DateTime now,
            bool forceOnHealthChange = false, float? observedHealth = null)
        {
            float health = observedHealth ?? ReadLiveHealthPercent(target);
            if (!s_histories.TryGetValue(target.NodeId, out var history))
            {
                history = new TargetHistory();
                s_histories[target.NodeId] = history;
            }

            history.LastSeenUtc = now;
            var samples = history.Samples;
            if (samples.Count > 0)
            {
                var previous = samples[samples.Count - 1];
                if (health > previous.HealthPercent &&
                    health - previous.HealthPercent < HealingResetPercent)
                {
                    health = previous.HealthPercent;
                }

                float healthChange = Math.Abs(previous.HealthPercent - health);
                if (health - previous.HealthPercent >= HealingResetPercent)
                {
                    samples.Clear();
                    history.HasTakenDamage = false;
                    history.LastDamageUtc = DateTime.MinValue;
                }
                else if (previous.HealthPercent - health >= 0.1f)
                {
                    history.HasTakenDamage = true;
                    history.LastDamageUtc = now;
                }

                if (samples.Count > 0 &&
                    (now - samples[samples.Count - 1].TimestampUtc).TotalSeconds < SampleIntervalSeconds &&
                    !(forceOnHealthChange && healthChange >= ForcedSampleDamagePercent))
                    return;
            }

            samples.Add(new HealthSample { TimestampUtc = now, HealthPercent = health });
            samples.RemoveAll(sample => (now - sample.TimestampUtc).TotalSeconds > SampleWindowSeconds);
        }

        private static bool EvaluateWillLiveFor(HeroCharacter target, double minimumSeconds,
            out TimeToDieEstimate estimate, out float liveHealth, out double healthFloor,
            out string reason)
        {
            estimate = TimeToDieEstimate.Unknown;
            liveHealth = 0;
            healthFloor = 0;

            if (target == null || target.IsDead || !target.IsEngagedWithPlayer())
            {
                reason = "invalid-target";
                return false;
            }

            try
            {
                liveHealth = ReadLiveHealthPercent(target);
                Sample(target, DateTime.UtcNow, true, liveHealth);
            }
            catch
            {
                reason = "health-read-failed";
                return false;
            }

            healthFloor = LiveHealthFloor(target, minimumSeconds);
            if (liveHealth < healthFloor)
            {
                reason = "live-health-floor";
                return false;
            }

            estimate = Estimate(target);
            if (estimate.State == TimeToDieState.Stable)
            {
                bool allowed = estimate.Seconds >= minimumSeconds;
                reason = allowed ? "stable-estimate" : "estimated-too-short";
                return allowed;
            }

            if (estimate.State == TimeToDieState.Stalled)
            {
                reason = "damage-stalled";
                return false;
            }

            bool fallbackAllowed = FallbackWillLiveFor(target, minimumSeconds, liveHealth);
            reason = fallbackAllowed ? "rank-fallback" : "learning-conservative";
            return fallbackAllowed;
        }

        private static float ReadLiveHealthPercent(HeroCharacter target)
        {
            float reported = Math.Max(0, Math.Min(100, target.HealthPercent));

            // Health/HealthMax fall back to a direct engine read when their cached fields are
            // unavailable. Use the lower valid value so a stale percentage can never make a dying
            // unit look healthier than the current absolute-health check.
            try
            {
                double current = Convert.ToDouble(target.Health);
                double maximum = Convert.ToDouble(target.HealthMax);
                if (current > 0 && maximum > 0)
                {
                    float absolute = (float)Math.Max(0, Math.Min(100, current * 100.0 / maximum));
                    return reported > 0 ? Math.Min(reported, absolute) : absolute;
                }
            }
            catch
            {
                // Percentage health remains a safe fallback on client builds where an absolute
                // health field temporarily disappears during an object-manager transition.
            }

            return reported;
        }

        private static double LiveHealthFloor(HeroCharacter target, double minimumSeconds)
        {
            double multiplier;
            double minimum;
            double maximum;

            switch (target.Toughness)
            {
                case cbtToughnessEnum.boss_2:
                case cbtToughnessEnum.boss_3:
                case cbtToughnessEnum.boss_4:
                case cbtToughnessEnum.boss_raid:
                    // Champions and bosses primarily trust measured TTD.
                    multiplier = 0.75;
                    minimum = 12;
                    maximum = 25;
                    break;

                case cbtToughnessEnum.boss_1:
                    // Gold elites are durable, but do not receive long setup at execute health.
                    multiplier = 3.0;
                    minimum = 5;
                    maximum = 50;
                    break;

                case cbtToughnessEnum.strong:
                    multiplier = 5.0;
                    minimum = 10;
                    maximum = 65;
                    break;

                case cbtToughnessEnum.player:
                    multiplier = 4.0;
                    minimum = 15;
                    maximum = 60;
                    break;

                default:
                    // For Crushing Darkness's six-second payoff this is a 60% hard floor.
                    multiplier = 10.0;
                    minimum = 15;
                    maximum = 85;
                    break;
            }

            return Math.Max(minimum, Math.Min(maximum, minimumSeconds * multiplier));
        }

        private static bool FallbackWillLiveFor(HeroCharacter target, double minimumSeconds, float health)
        {
            if (target.BossOrGreater())
            {
                double requiredHealth = Math.Max(10, Math.Min(70, minimumSeconds * 1.5));
                return health >= requiredHealth;
            }

            if (target.StrongOrGreater())
            {
                double requiredHealth = Math.Max(20, Math.Min(75, minimumSeconds * 3.0));
                return health >= requiredHealth;
            }

            // Standard leveling enemies only receive short channels while an estimate is learning;
            // long cooldowns and DoTs wait for a proven lifetime or a tougher target.
            return minimumSeconds <= 3.5 && health >= 40;
        }

        private static void LogDecision(HeroCharacter target, string abilityName, double minimumSeconds,
            bool allowed, TimeToDieEstimate estimate, float liveHealth, double healthFloor, string reason)
        {
            if (target == null || string.IsNullOrEmpty(abilityName))
                return;

            var now = DateTime.UtcNow;
            string key = target.NodeId + ":" + abilityName;
            string stateToken = allowed + ":" + reason + ":" + estimate.State;
            if (s_decisionLogs.TryGetValue(key, out var previous) &&
                previous.StateToken == stateToken &&
                (now - previous.LastUtc).TotalSeconds < DiagnosticRepeatSeconds)
            {
                return;
            }

            s_decisionLogs[key] = new DecisionLog { LastUtc = now, StateToken = stateToken };
            string estimatedSeconds = double.IsInfinity(estimate.Seconds)
                ? "n/a"
                : estimate.Seconds.ToString("F1");
            string targetName = "?";
            try
            {
                if (!string.IsNullOrEmpty(target.Name))
                    targetName = target.Name;
            }
            catch
            {
                // The combat decision is already complete; a despawning name must not break it.
            }
            Logging.WriteDiagnostic(
                "[TTD] {0} {1} on {2}: hp={3:F1}% floor={4:F1}% estimate={5} state={6} observed={7:F1}% span={8:F1}s required={9:F1}s reason={10}",
                abilityName,
                allowed ? "ALLOW" : "BLOCK",
                targetName,
                liveHealth,
                healthFloor,
                estimatedSeconds,
                estimate.State,
                estimate.ObservedDamagePercent,
                estimate.SampleSpanSeconds,
                minimumSeconds,
                reason);
        }
    }
}
