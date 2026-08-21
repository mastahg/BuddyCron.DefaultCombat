// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Behaviors
{
    /// <summary>Behavior-tree building blocks for casting: buffs, casts, DoTs, heals and
    /// ground-targeted abilities, plus the shared DoT blacklist.</summary>
    public static class Spell
    {
        /// <summary>Selects a value from the behavior-tree context.</summary>
        public delegate T Selection<out T>(object context);

        /// <summary>Selects the unit a cast should be aimed at.</summary>
        public delegate HeroCharacter UnitSelectionDelegate(object context);

        /// <summary>Spell+target pairs temporarily excluded from casting (entries expire on their own).</summary>
        public static List<ExpiringItem> BlackListedSpells = new List<ExpiringItem>();

        private static readonly Dictionary<string, DateTime> s_castFailureLogs =
            new Dictionary<string, DateTime>();
        private static DateTime _lastGroundCastFailureUtc = DateTime.MinValue;
        private static DateTime _observedCastStartUtc = DateTime.MinValue;
        private static ulong _observedCastSpecId;

        /// <summary>Composite that succeeds while the player is casting, blocking lower-priority
        /// actions in the selector.</summary>
        public static Composite WaitForCast()
        {
            return new Action(ret =>
            {
                if (Core.Player == null || !Core.Player.IsCasting)
                {
                    _observedCastStartUtc = DateTime.MinValue;
                    _observedCastSpecId = 0;
                    return RunStatus.Failure;
                }

                ulong abilitySpecId = Core.Player.CastingAbilitySpecId;
                if (_observedCastStartUtc == DateTime.MinValue ||
                    abilitySpecId != _observedCastSpecId)
                {
                    _observedCastStartUtc = DateTime.UtcNow;
                    _observedCastSpecId = abilitySpecId;
                }

                double maximumWaitSeconds = Math.Max(0.75, Core.Player.CastTimeTotal + 1.0);
                if ((DateTime.UtcNow - _observedCastStartUtc).TotalSeconds <= maximumWaitSeconds)
                    return RunStatus.Success;

                string castName = Core.Player.CastingAbility != null
                    ? Core.Player.CastingAbility.Name
                    : "unknown";
                AbilityManager.StopCasting(ablCancelReasonEnum.Manual);
                Logger.Write("[CastRecovery] Cleared stale cast: " + castName);
                _observedCastStartUtc = DateTime.MinValue;
                _observedCastSpecId = 0;
                return RunStatus.Failure;
            });
        }

        #region Buff

        /// <summary>Casts <paramref name="spell"/> on the player when the matching buff is missing
        /// and <paramref name="reqs"/> (if any) passes.</summary>
        public static Composite Buff(string spell, Selection<bool> reqs = null)
        {
            return
                new Decorator(
                    ret => (reqs == null || reqs(ret)) && !Core.Player.HasBuff(spell),
                    Cast(spell, ret => Core.Player, ret => true));
        }

        #endregion

        #region Cast

        /// <summary>Casts <paramref name="spell"/> on the current target.</summary>
        public static Composite Cast(string spell, Selection<bool> reqs = null)
        {
            return Cast(spell, ret => Core.Player.Target, reqs);
        }

        /// <summary>Casts <paramref name="spell"/> on the unit chosen by <paramref name="onUnit"/>
        /// when <paramref name="reqs"/> passes and the ability is currently usable.</summary>
        public static Composite Cast(string spell, UnitSelectionDelegate onUnit, Selection<bool> reqs = null)
        {
            return new Action(ret =>
            {
                if (onUnit == null || (reqs != null && !reqs(ret)))
                    return RunStatus.Failure;

                var target = onUnit(ret);
                if (target == null || !AbilityManager.CanCast(spell, target).Success)
                    return RunStatus.Failure;

                StopMovementForCast(spell);
                var result = AbilityManager.Cast(spell, target);
                if (!result.Success)
                {
                    LogCastFailure(spell, target, result.ToString());
                    return RunStatus.Failure;
                }

                Logger.Write(">> Casting <<   " + spell);
                return RunStatus.Success;
            });
        }

        /// <summary>Casts the ground-targeted <paramref name="spell"/> at the current target's location.</summary>
        public static Composite CastOnGround(string spell, Selection<bool> reqs = null)
        {
            return
                new Decorator(
                    ret =>
                        (reqs == null || reqs(ret)) &&
                        (Targeting.AoeDpsPoint != Vector3.Zero || Core.Player.Target != null),
                    CastOnGround(spell,
                        ctx => Targeting.AoeDpsPoint != Vector3.Zero
                            ? Targeting.AoeDpsPoint
                            : Core.Player.Target.Location,
                        ctx => true));
        }

        /// <summary>Casts the ground-targeted <paramref name="spell"/> at the point returned by
        /// <paramref name="location"/> (skipped when it yields <see cref="Vector3.Zero"/>).</summary>
        public static Composite CastOnGround(string spell, BuddyCron.Behaviors.ValueRetriever<Vector3> location, Selection<bool> reqs = null)
        {
            return
                new Decorator(
                    ret =>
                        (reqs == null || reqs(ret)) && location != null && location(ret) != Vector3.Zero &&
                        AbilityManager.HasAbility(spell),
                    new Action(ret =>
                    {
                        Vector3 groundTarget = location(ret);
                        StopMovementForCast(spell);
                        var castResult = AbilityManager.Cast(spell, groundTarget);
                        if (castResult.Success)
                        {
                            Logger.Write(">> Casting on Ground <<   " + spell);
                            return RunStatus.Success;
                        }

                        if ((DateTime.UtcNow - _lastGroundCastFailureUtc).TotalSeconds >= 3)
                        {
                            _lastGroundCastFailureUtc = DateTime.UtcNow;
                            Logger.Write("[Ground Cast] {0} failed at {1}; known={2}, moving={3}, result={4}",
                                spell, groundTarget, AbilityManager.HasAbility(spell), Core.Player.IsMoving, castResult);
                        }

                        return RunStatus.Failure;
                    }));
        }

        internal static void StopMovementForCast(string spell)
        {
            if (RotationRuntime.MovementDisabled ||
                RoutineManager.IsAnyDisallowed(CapabilityFlags.Movement) ||
                !Core.Player.IsMoving)
            {
                return;
            }

            var ability = AbilityManager.KnownAbilities.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, spell, StringComparison.Ordinal));
            if (ability != null && (ability.CastingTime > 0 || ability.ChannelingTime > 0))
                MovementManager.MoveStop();
        }

        internal static void LogCastFailure(string spell, HeroCharacter target, string result)
        {
            string key = spell + ":" + (target != null ? target.NodeId.ToString() : "none");
            var now = DateTime.UtcNow;
            if (s_castFailureLogs.TryGetValue(key, out var lastUtc) &&
                (now - lastUtc).TotalSeconds < 2)
            {
                return;
            }

            s_castFailureLogs[key] = now;
            Logger.Write("[CastFailed] " + spell + " on " +
                         (target != null ? target.Name : "none") + ": " + result);
        }

        /// <summary>Ground-targeted DoT on the current target; see the unit-selecting overload.</summary>
        public static Composite DoTGround(string spell, float time = 0, Selection<bool> reqs = null)
        {
            return DoTGround(spell, ret => Core.Player.Target, time, reqs);
        }


        /// <summary>Casts the ground-targeted <paramref name="spell"/> at the chosen unit's location,
        /// then blacklists the spell/target pair for the spell's cooldown plus <paramref name="time"/>
        /// milliseconds so it is not reapplied early.</summary>
        public static Composite DoTGround(string spell, UnitSelectionDelegate onUnit, float time, Selection<bool> reqs = null)
        {
            return new Action(ret =>
            {
                if (onUnit == null || (reqs != null && !reqs(ret)))
                    return RunStatus.Failure;

                var target = onUnit(ret);
                if (target == null ||
                    SpellBlackListed(spell, target.NodeId) ||
                    !AbilityManager.CanCast(spell, target).Success)
                {
                    return RunStatus.Failure;
                }

                StopMovementForCast(spell);
                var result = AbilityManager.Cast(spell, target.Location);
                if (!result.Success)
                {
                    LogCastFailure(spell, target, result.ToString());
                    return RunStatus.Failure;
                }

                BlackListedSpells.Add(
                    new ExpiringItem(spell, GetCooldown(spell) + 25 + time, target.NodeId));
                Logger.Write(">> Casting on Ground <<   " + spell);
                return RunStatus.Success;
            });
        }

        #endregion

        #region DoT

        /// <summary>DoT on the current target; see the unit-selecting overload.</summary>
        public static Composite DoT(string spell, string debuff, float time = 0, Selection<bool> reqs = null)
        {
            return DoT(spell, ret => Core.Player.Target, debuff, time, reqs);
        }

        /// <summary>Casts <paramref name="spell"/> when the player's <paramref name="debuff"/> is not
        /// on the chosen unit, then blacklists the spell/target pair for the spell's cooldown plus
        /// <paramref name="time"/> milliseconds so the DoT is not clipped.</summary>
        public static Composite DoT(string spell, UnitSelectionDelegate onUnit, string debuff, float time,
            Selection<bool> reqs = null)
        {
            return new Action(ret =>
            {
                if (onUnit == null || (reqs != null && !reqs(ret)))
                    return RunStatus.Failure;

                var target = onUnit(ret);
                if (target == null ||
                    target.HasMyDebuff(debuff) ||
                    SpellBlackListed(spell, target.NodeId) ||
                    !AbilityManager.CanCast(spell, target).Success)
                {
                    return RunStatus.Failure;
                }

                StopMovementForCast(spell);
                var result = AbilityManager.Cast(spell, target);
                if (!result.Success)
                {
                    LogCastFailure(spell, target, result.ToString());
                    return RunStatus.Failure;
                }

                BlackListedSpells.Add(
                    new ExpiringItem(spell, GetCooldown(spell) + 25 + time, target.NodeId));
                Logger.Write(">> Casting <<   " + spell);
                return RunStatus.Success;
            });
        }

        /// <summary>Cast time in milliseconds of the first known ability whose name contains
        /// <paramref name="spell"/> (0 for instants).</summary>
        public static float GetCastTime(string spell)
        {
            var ability = AbilityManager.KnownAbilities.FirstOrDefault(a => a.Name.Contains(spell));
            return ability != null ? ability.CastingTime * 1000 : 0;
        }

        /// <summary>Cooldown in milliseconds of the first known ability whose name contains
        /// <paramref name="spell"/>.</summary>
        public static float GetCooldown(string spell)
        {
            var ability = AbilityManager.KnownAbilities.FirstOrDefault(a => a.Name.Contains(spell));
            return ability != null ? ability.CooldownTime * 1000 : 0;
        }

        /// <summary>True while <paramref name="spell"/> is blacklisted against the target identified
        /// by <paramref name="guid"/>; expired entries are pruned on each call.</summary>
        public static bool SpellBlackListed(string spell, ulong guid)
        {
            BlackListedSpells.RemoveAll(item => item.IsExpired);
            return BlackListedSpells.Any(item =>
                string.Equals(item.Item, spell, StringComparison.Ordinal) &&
                item.TargetGuid == guid);
        }

        #endregion

        #region Heal

        /// <summary>Casts the cleanse <paramref name="spell"/> on the current dispel target.</summary>
        public static Composite Cleanse(string spell, Selection<bool> reqs = null)
        {
            return new Decorator(
                ret => Targeting.DispelTarget != null && (reqs == null || reqs(ret)),
                Cast(spell, ret => Targeting.DispelTarget, reqs));
        }


        /// <summary>Heals the current heal target when at or below <paramref name="hp"/> percent health.</summary>
        public static Composite Heal(string spell, int hp = 100, Selection<bool> reqs = null)
        {
            return Heal(spell, onUnit => Targeting.HealTarget, hp, reqs);
        }

        /// <summary>Heals the chosen unit when at or below <paramref name="hp"/> percent health.</summary>
        public static Composite Heal(string spell, UnitSelectionDelegate onUnit, int hp = 100, Selection<bool> reqs = null)
        {
            return new Decorator(
                ret => onUnit != null && onUnit(ret) != null && (reqs == null || reqs(ret)) &&
                       onUnit(ret).HealthPercent <= hp,
                Cast(spell, onUnit, reqs));
        }

        /// <summary>Casts the AoE heal <paramref name="spell"/> on the computed AoE heal target when
        /// enough allies are injured.</summary>
        public static Composite HealAoe(string spell, Selection<bool> reqs = null)
        {
            return new Decorator(
                ret => (reqs == null || reqs(ret)) && Targeting.ShouldAoeHeal && Targeting.AoeHealTarget != null,
                Cast(spell, onUnit => Targeting.AoeHealTarget, reqs));
        }

        /// <summary>Heal-over-time on the current heal target; see the unit-selecting overload.</summary>
        public static Composite HoT(string spell, int hp = 100, Selection<bool> reqs = null)
        {
            return new Decorator(ret => Targeting.HealTarget != null,
                HoT(spell, onUnit => Targeting.HealTarget, hp, reqs));
        }

        /// <summary>Applies the heal-over-time <paramref name="spell"/> when the chosen unit lacks the
        /// player's buff and is at or below <paramref name="hp"/> percent health.</summary>
        public static Composite HoT(string spell, UnitSelectionDelegate onUnit, int hp = 100, Selection<bool> reqs = null)
        {
            return new Decorator(
                ret =>
                    onUnit != null && onUnit(ret) != null && (reqs == null || reqs(ret)) && !onUnit(ret).HasMyBuff(spell) &&
                    onUnit(ret).HealthPercent <= hp,
                Cast(spell, onUnit, reqs));
        }

        /// <summary>Casts the ground-targeted AoE heal <paramref name="spell"/> at the computed AoE
        /// heal point when enough allies are injured.</summary>
        public static Composite HealGround(string spell, CanRunDecoratorDelegate reqs = null)
        {
            return new Decorator(
                ret => Targeting.AoeHealPoint != Vector3.Zero && (reqs == null || reqs(ret)) &&
                       Targeting.ShouldAoeHeal,
                CastOnGround(spell, ret => Targeting.AoeHealPoint, ret => true));
        }

        #endregion
    }

    /// <summary>Blacklist entry that expires using a monotonic timestamp.</summary>
    public class ExpiringItem
    {
        /// <summary>The blacklisted spell name.</summary>
        public string Item;
        /// <summary>The target the spell is blacklisted against.</summary>
        public ulong TargetGuid;
        private readonly long _expiresAt;

        public bool IsExpired => Stopwatch.GetTimestamp() >= _expiresAt;

        /// <summary>Blacklists <paramref name="str"/> against target <paramref name="g"/> for
        /// <paramref name="milisecs"/> milliseconds.</summary>
        public ExpiringItem(string str, float milisecs, ulong g)
        {
            Item = str;
            TargetGuid = g;
            double durationSeconds = Math.Max(0, milisecs) / 1000d;
            _expiresAt = Stopwatch.GetTimestamp() +
                         (long)(durationSeconds * Stopwatch.Frequency);
        }
    }
}
