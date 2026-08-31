using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BuddyCron;
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
        private const int SampleCapacity = (int)(SampleWindowSeconds / SampleIntervalSeconds) + 1;

        private readonly record struct Sample(DateTime Time, float Health);

        private sealed class History
        {
            public readonly Queue<Sample> Samples = new Queue<Sample>();

            public Sample Oldest => Samples.Peek();
            public Sample Newest { get; private set; }

            public void Add(Sample sample)
            {
                if (Samples.Count > 0 && (sample.Time - Newest.Time).TotalSeconds > SampleWindowSeconds)
                    Samples.Clear();

                Samples.Enqueue(sample);
                Newest = sample;

                while (Samples.Count > SampleCapacity ||
                       (sample.Time - Samples.Peek().Time).TotalSeconds > SampleWindowSeconds)
                {
                    Samples.Dequeue();
                }
            }

            public Sample AtLeastSecondsBeforeNewest(double seconds)
            {
                var candidate = Oldest;
                foreach (var sample in Samples)
                {
                    if ((Newest.Time - sample.Time).TotalSeconds < seconds)
                        break;
                    candidate = sample;
                }

                return candidate;
            }
        }

        private static readonly ConditionalWeakTable<HeroCharacter, History> s_histories =
            new ConditionalWeakTable<HeroCharacter, History>();

        public static bool WillLiveFor(this HeroCharacter target, double minimumSeconds)
        {
            if (target == null || !target.IsValid || target.IsDead)
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
            var history = s_histories.GetValue(target, _ => new History());
            var hasSamples = history.Samples.Count > 0;
            var last = history.Newest;
            if (!hasSamples || (now - last.Time).TotalSeconds >= SampleIntervalSeconds ||
                last.Health - health >= 0.25f)
            {
                history.Add(new Sample(now, health));
            }

            if (history.Samples.Count < 2)
                return null;

            var first = history.Oldest;
            var newest = history.Newest;
            var recent = history.AtLeastSecondsBeforeNewest(RecentWindowSeconds);
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
            var reported = MathF.Max(0, MathF.Min(100, target.HealthPercent));
            try
            {
                var current = target.Health;
                var maximum = target.HealthMax;
                if (current > 0 && maximum > 0)
                {
                    var live = MathF.Max(0, MathF.Min(100, current * 100f / maximum));
                    return reported > 0 ? MathF.Min(reported, live) : live;
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
