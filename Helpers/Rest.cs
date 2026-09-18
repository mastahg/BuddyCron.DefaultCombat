// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Threading.Tasks;
using BuddyCron;
using BuddyCron.Behaviors;
using BuddyCron.Managers;
using BuddyCron.Navigation;
using BuddyCron.Objects;
using Reborn.Behaviors.Coroutines;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Helpers
{
    /// <summary>Out-of-combat recovery: revives dead companions and channels the class rest ability
    /// until health/resource are restored.</summary>
    public static class Rest
    {
        // Live measurement (Sentinel channeling Introspection, two consecutive rests): the rest
        // channel restores health in ~1s ticks worth ~7.6 percentage points of max health each
        // (88.2 -> 95.9 -> 100.0 over 1.8s, and 82.3 -> 89.8 -> 97.5 over 2.2s). Passive
        // out-of-combat regeneration moved health 0 points over the same windows, so the channel
        // is the only thing that decides how long a rest runs.
        private const double RecoveryPercentPerSecond = 7.6;

        // Resting is not free: it stops the character, spends a GCD on the channel and cancels it
        // again. Under this much channel time the churn costs more than the sliver of health it
        // hands back, so the rest is skipped and the deficit is carried into the next pull. At the
        // measured rate this is a deficit of ~23 points, i.e. resting from 77% health or lower.
        private const double MinimumRestSeconds = 3.0;

        /// <summary>True while the rest channel is actually running.</summary>
        public static bool IsResting { get; private set; }

        /// <summary>True while a rest is running or is about to start. Anything that would cancel
        /// the channel — re-stealthing above all — must hold off while this is set.</summary>
        public static bool IsRestPending => IsResting || NeedRest();

        /// <summary>Composite that first revives a dead companion, then rests until health and
        /// resource are back to full.</summary>
        public static Composite HandleRest
        {
            get
            {
                return new PrioritySelector(
                    new ActionRunCoroutine(ctx => ReviveCompanion()),
                    new ActionRunCoroutine(ctx => Rejuvenate())
                    );
            }
        }

        /// <summary>Revives a dead companion: waits out any in-flight cast, closes to interact
        /// range, then channels Revive Companion. Runs one step per tick (Running while moving /
        /// casting); false when there is nothing to revive, so the selector falls through.</summary>
        private static async Task<bool> ReviveCompanion()
        {
            var companion = Core.Player.Companion;
            if (companion == null || !companion.IsDead)
                return false;

            // don't clip whatever is already casting (the old Spell.WaitForCast guard)
            if (Core.Player.IsCasting)
                return true;

            if (companion.Distance > 0.3f)
            {
                await CommonTasks.MoveAndStop(new MoveToParameters(companion.Location, "Companion"), 0.2f, true, "Companion");
                return true;
            }

            return AbilityManager.Cast("Revive Companion", companion).Success;
        }

        /// <summary>Channels the class rest ability until health/resource are back to full.
        /// Earlier implementations parked the bot thread in a while+Thread.Sleep loop and cancelled
        /// the finished channel by sending ESC to the game window; this yields to the tree between
        /// polls (the composite reports Running) and cancels through <see cref="AbilityManager.StopCasting"/>,
        /// so it needs neither the window handle nor focus.</summary>
        private static async Task<bool> Rejuvenate()
        {
            if (!NeedRest())
                return false;

            IsResting = true;
            try
            {
                Logger.Write($"Starting to rest ({ProjectedRestSeconds():F1}s of channel to top off)!");

                if (Core.Player.IsMoving)
                {
                    Navigator.PlayerMover.MoveStop();
                    await Coroutine.Wait(300, () => !Core.Player.IsMoving);
                }

                await Coroutine.Wait(1000, () => AbilityManager.CanCast(Core.Player.RejuvenateAbilityName(), Core.Player).Success);

                while (KeepResting())
                {
                    if (!Core.Player.IsCasting && !AbilityManager.Cast(Core.Player.RejuvenateAbilityName(), Core.Player).Success)
                    {
                        // channel refused (combat, unknown ability name, ...) — bail instead of spinning
                        return false;
                    }

                    await Coroutine.Sleep(100);
                }

                Logger.Write("Finished Resting");
                // fully rested (or interrupted via KeepResting going false) — stop the channel
                if (Core.Player.IsCasting)
                    AbilityManager.StopCasting();

                return true;
            }
            finally
            {
                IsResting = false;
            }
        }

        /// <summary>Player resource scaled so low values mean "needs rest"; rage/focus classes
        /// always report 100 (they never rest for resource).</summary>
        public static int NormalizedResource()
        {
            // ResourcePercent() returns remaining resource, 100 = full/rested — heat classes
            // count down on the current build, so no class needs inverting here.
            switch (Core.Player.AdvancedClass)
            {
                case AdvancedClass.Juggernaut:
                case AdvancedClass.Marauder:
                case AdvancedClass.Guardian:
                case AdvancedClass.Sentinel:
                    return 100;
                default:
                    return (int)Core.Player.ResourcePercent();
            }
        }

        /// <summary>True when out of combat, the player's health/resource (or the companion's
        /// health) are low enough to start resting, and the rest would actually be worth starting
        /// — see <see cref="ProjectedRestSeconds"/>.</summary>
        public static bool NeedRest()
        {
            if (RotationRuntime.MovementDisabled || Core.Player.InCombat)
                return false;

            var lowEnough = NormalizedResource() < 50 || Core.Player.HealthPercent < 90 ||
                            Core.Player.Companion is { IsDead: false, HealthPercent: < 90 };

            return lowEnough && ProjectedRestSeconds() >= MinimumRestSeconds;
        }

        /// <summary>Seconds of channelling a rest started now would take: the slowest of the stats
        /// the channel has to bring back to full, since <see cref="KeepResting"/> holds the channel
        /// until every one of them is topped off.</summary>
        private static double ProjectedRestSeconds()
        {
            var seconds = SecondsToFull(Core.Player.HealthPercent);
            seconds = Math.Max(seconds, SecondsToFull(NormalizedResource()));

            if (Core.Player.Companion is { IsDead: false } companion)
                seconds = Math.Max(seconds, SecondsToFull(companion.HealthPercent));

            return seconds;
        }

        /// <summary>Channel time needed to take one stat from <paramref name="percent"/> to full.</summary>
        private static double SecondsToFull(double percent)
        {
            var deficit = 100.0 - percent;
            return deficit <= 0.0 ? 0.0 : deficit / RecoveryPercentPerSecond;
        }

        /// <summary>True while resting should continue: still out of combat and anything
        /// (health, resource, companion health) below 100%.</summary>
        private static bool KeepResting()
        {
            var resource = NormalizedResource();
            return !RotationRuntime.MovementDisabled && !Core.Player.InCombat && (resource < 100 || Core.Player.HealthPercent < 100 || Core.Player.Companion is
            {
                IsDead: false, HealthPercent: < 100
            });
        }
    }
}
