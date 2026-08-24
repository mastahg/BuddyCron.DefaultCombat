using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron.Objects;
using Reborn.Utilities;

namespace DefaultCombat.Helpers
{
    /// <summary>Estimates whether a target will live long enough for a sustained ability to pay off.</summary>
    public static class TimeToDie
    {
        private const double SampleIntervalSeconds = 0.25;
        private const double SampleWindowSeconds = 5;
        private const double RecentWindowSeconds = 2;
        private const double MinimumSampleSpanSeconds = 1.25;
        private const double HistoryRetentionSeconds = 15;

        private sealed class Sample
        {
            public DateTime Time;
            public float Health;
        }

        private sealed class History
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public DateTime LastSeen;
        }

        private static readonly Dictionary<ulong, History> s_histories =
            new Dictionary<ulong, History>();

        public static bool WillLiveFor(HeroCharacter target, double minimumSeconds)
        {
            if (target == null || target.IsDead)
                return false;

            var health = ReadHealthPercent(target);
            if (health < HealthFloor(target, minimumSeconds))
                return false;

            var estimate = Estimate(target, health);
            if (estimate.HasValue)
                return estimate.Value >= minimumSeconds;

            return FallbackWillLiveFor(target, minimumSeconds, health);
        }

        private static double? Estimate(HeroCharacter target, float health)
        {
            var now = DateTime.UtcNow;
            if (!s_histories.TryGetValue(target.NodeId, out var history))
            {
                history = new History();
                s_histories[target.NodeId] = history;
            }

            history.LastSeen = now;
            var samples = history.Samples;
            var last = samples.LastOrDefault();
            if (last == null || (now - last.Time).TotalSeconds >= SampleIntervalSeconds ||
                last.Health - health >= 0.25f)
            {
                samples.Add(new Sample { Time = now, Health = health });
            }

            samples.RemoveAll(sample => (now - sample.Time).TotalSeconds > SampleWindowSeconds);
            foreach (var id in s_histories
                .Where(pair => (now - pair.Value.LastSeen).TotalSeconds > HistoryRetentionSeconds)
                .Select(pair => pair.Key)
                .ToList())
            {
                s_histories.Remove(id);
            }

            if (samples.Count < 2)
                return null;

            var first = samples[0];
            var newest = samples[samples.Count - 1];
            var recent = samples.LastOrDefault(sample =>
                (newest.Time - sample.Time).TotalSeconds >= RecentWindowSeconds) ?? first;
            var span = (newest.Time - first.Time).TotalSeconds;
            if (span < MinimumSampleSpanSeconds)
                return null;

            var longRate = LossPerSecond(first, newest);
            var recentRate = LossPerSecond(recent, newest);
            var rate = Math.Max(longRate, recentRate);
            return rate > 0.05 ? newest.Health / rate : (double?)null;
        }

        private static double LossPerSecond(Sample first, Sample last)
        {
            var span = (last.Time - first.Time).TotalSeconds;
            var damage = first.Health - last.Health;
            return span > 0 && damage >= 1 ? damage / span : 0;
        }

        private static float ReadHealthPercent(HeroCharacter target)
        {
            var reported = Math.Max(0, Math.Min(100, target.HealthPercent));
            try
            {
                var current = Convert.ToDouble(target.Health);
                var maximum = Convert.ToDouble(target.HealthMax);
                if (current > 0 && maximum > 0)
                {
                    var live = (float)Math.Max(0, Math.Min(100, current * 100 / maximum));
                    return reported > 0 ? Math.Min(reported, live) : live;
                }
            }
            catch
            {
                // Some client builds temporarily lose the absolute health fields during despawn.
            }

            return reported;
        }

        private static double HealthFloor(HeroCharacter target, double minimumSeconds)
        {
            switch (target.Toughness)
            {
                case cbtToughnessEnum.boss_2:
                case cbtToughnessEnum.boss_3:
                case cbtToughnessEnum.boss_4:
                case cbtToughnessEnum.boss_raid:
                    return Math.Max(12, Math.Min(25, minimumSeconds * 0.75));
                case cbtToughnessEnum.boss_1:
                    return Math.Max(5, Math.Min(50, minimumSeconds * 3));
                case cbtToughnessEnum.strong:
                    return Math.Max(10, Math.Min(65, minimumSeconds * 5));
                default:
                    return Math.Max(15, Math.Min(85, minimumSeconds * 10));
            }
        }

        private static bool FallbackWillLiveFor(HeroCharacter target, double minimumSeconds, float health)
        {
            if (target.BossOrGreater())
                return health >= Math.Max(10, Math.Min(70, minimumSeconds * 1.5));
            if (target.StrongOrGreater())
                return health >= Math.Max(20, Math.Min(75, minimumSeconds * 3));

            return minimumSeconds <= 3.5 && health >= 40;
        }
    }
}
