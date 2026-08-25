// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;
using Timer = System.Timers.Timer;

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

        /// <summary>Composite that succeeds while the player is casting, blocking lower-priority
        /// actions in the selector.</summary>
        public static Composite WaitForCast()
        {
            return new Decorator(ret => Core.Player.IsCasting, new Action(ret => RunStatus.Success));
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
            return
                new Decorator(ret => onUnit != null && onUnit(ret) != null && (reqs == null || reqs(ret)) && AbilityManager.CanCast(spell, onUnit(ret)).Success,
                        new Action(ret =>
                        {
                            //added current target health percent check
                            Logger.Write(">> Casting <<   " + spell);
                            AbilityManager.Cast(spell, onUnit(ret));
                            
                        })
                    );
        }

        /// <summary>Casts the ground-targeted <paramref name="spell"/> at the current target's location.</summary>
        public static Composite CastOnGround(string spell, Selection<bool> reqs = null)
        {
            return
                new Decorator(
                    ret =>
                        (reqs == null || reqs(ret)) && Core.Player.Target != null,
                    CastOnGround(spell, ctx => Core.Player.Target.Location, ctx => true));
        }

        /// <summary>Casts the ground-targeted <paramref name="spell"/> at the point returned by
        /// <paramref name="location"/> (skipped when it yields <see cref="Vector3.Zero"/>).</summary>
        public static Composite CastOnGround(string spell, BuddyCron.Behaviors.ValueRetriever<Vector3> location, Selection<bool> reqs = null)
        {
            return
                new Decorator(
                    ret =>
                        (reqs == null || reqs(ret)) && location != null && location(ret) != Vector3.Zero &&
                        AbilityManager.CanCast(spell, Core.Player.Target ?? Core.Player).Success,
                    new Action(ret => { AbilityManager.Cast(spell, location(ret)); }));
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
            return
                new Decorator(
                    ret => (reqs == null || reqs(ret))
                           && onUnit != null
                           && onUnit(ret) != null
                           && AbilityManager.CanCast(spell, onUnit(ret)).Success
                           && !SpellBlackListed(spell, onUnit(ret).NodeId),
                    new PrioritySelector(
                        new Action(ctx =>
                        {
                            BlackListedSpells.Add(new ExpiringItem(spell, GetCooldown(spell) + 25 + time, onUnit(ctx).NodeId));
                            Logger.Write(">> Casting on Ground <<   " + spell);
                            return RunStatus.Failure;
                        }),
                        new Action(ret => { AbilityManager.Cast(spell, onUnit(ret).Location); })));
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
            return
                new Decorator(
                    ret => onUnit != null && onUnit(ret) != null && (reqs == null || reqs(ret))
                           && !onUnit(ret).HasMyDebuff(debuff)
                           && AbilityManager.CanCast(spell, onUnit(ret)).Success
                           && !SpellBlackListed(spell, onUnit(ret).NodeId),
                    new PrioritySelector(
                        new Action(ctx =>
                        {
                            BlackListedSpells.Add(new ExpiringItem(spell, GetCooldown(spell) + 25 + time, onUnit(ctx).NodeId));
                            Logger.Write(">> Casting <<   " + spell);
                            return RunStatus.Failure;
                        }),
                        new Action(ret => { AbilityManager.Cast(spell, onUnit(ret)); })));
        }

        /// <summary>Cast time in milliseconds of the first known ability whose name contains
        /// <paramref name="spell"/> (0 for instants).</summary>
        public static float GetCastTime(string spell)
        {
            float castTime = 0;
            var v = AbilityManager.KnownAbilities.FirstOrDefault(a => a.Name.Contains(spell)).CastingTime;
            castTime += v * 1000;
            return castTime;
        }

        /// <summary>Cooldown in milliseconds of the first known ability whose name contains
        /// <paramref name="spell"/>.</summary>
        public static float GetCooldown(string spell)
        {
            float time = 0;
            var v = AbilityManager.KnownAbilities.FirstOrDefault(a => a.Name.Contains(spell)).CooldownTime;
            time += v * 1000;
            return time;
        }

        /// <summary>True while <paramref name="spell"/> is blacklisted against the target identified
        /// by <paramref name="guid"/>; expired entries are pruned on each call.</summary>
        public static bool SpellBlackListed(string spell, float guid)
        {
            BlackListedSpells.RemoveAll(s => s.Item.Equals(""));
            return BlackListedSpells.Any(s => s.Item.Equals(spell) && Math.Abs(s.TargetGuid - guid) < .01f);
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

    /// <summary>Blacklist entry that voids itself (blanks <see cref="Item"/>) after a timer elapses.</summary>
    public class ExpiringItem
    {
        /// <summary>The blacklisted spell name; empty once the entry expires.</summary>
        public string Item;
        /// <summary>The target the spell is blacklisted against.</summary>
        public ulong TargetGuid;

        /// <summary>Blacklists <paramref name="str"/> against target <paramref name="g"/> for
        /// <paramref name="milisecs"/> milliseconds.</summary>
        public ExpiringItem(string str, float milisecs, ulong g)
        {
            Item = str;
            var t = new Timer(milisecs);
            TargetGuid = g;
            t.Elapsed += Elapsed_Event;
            t.Start();
        }

        private void Elapsed_Event(object sender, System.Timers.ElapsedEventArgs e)
        {
            Item = "";
        }
    }
}
