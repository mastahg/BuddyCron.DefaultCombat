// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using BuddyCron.Behaviors;
using BuddyCron.Helpers;
using BuddyCron.Managers;
using BuddyCron.Navigation;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using Reborn.Utilities;
using Reborn.Behaviors.Treesharp;
using DefaultCombat.Helpers;
using Targeting = DefaultCombat.Behaviors.Targeting;

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     Operative Concealment (burst melee dps) rotation: stealth Backstab opener, poison
    ///     upkeep detonated by Volatile Substance, Tactical Advantage spent on Laceration.
    /// </summary>
    public class Concealment : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Concealment;

        public override string Name => "Operative Concealment";

        /// <summary>
        ///     While stealthed with Backstab available we hold everything else so the opener is
        ///     always the (guaranteed-crit, TA-generating) stealth Backstab. If Backstab is on
        ///     cooldown we drop the hold rather than stall out of stealth.
        /// </summary>
        private bool HoldForOpener => Core.Player.IsStealthed && Core.Player.Target != null &&
                                             AbilityManager.CanCast("Backstab", Core.Player.Target).Success;

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Coordination"),
					Spell.Buff("Stealth", ret => !Rest.KeepResting() && !RotationRuntime.MovementDisabled && !Core.Player.IsMounted)
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Escape", ret => Core.Player.IsStunned),

                    //Raid buff - costs 1 Tactical Advantage
                    Spell.Buff("Tactical Superiority", ret => CombatHotkeys.EnableRaidBuffs && Core.Player.HasBuff("Tactical Advantage")),

                    //Energy / Tactical Advantage economy
                    Spell.Cast("Adrenaline Probe", ret => Core.Player.EnergyPercent <= 45),
                    Spell.Cast("Stim Boost", ret => Core.Player.InCombat && Core.Player.BuffCount("Tactical Advantage") < 2),

                    //Defensives
                    Spell.Buff("Shield Probe", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Evasion", ret => Core.Player.HealthPercent <= 50),

                    //Off-heals (Operatives keep these in every spec)
                    Spell.Cast("Kolto Infusion", on => Core.Player, ret => Core.Player.HealthPercent <= 40 && Core.Player.HasBuff("Tactical Advantage")),
                    Spell.HoT("Kolto Probe", on => Core.Player, 60),

                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Holotraverse", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance > .4f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Energy floor - free filler while Adrenaline Probe is down
                    Spell.Cast("Rifle Shot",
                        ret => Core.Player.EnergyPercent < 45 && !AbilityManager.CanCast("Adrenaline Probe", Core.Player).Success),

                    //Guarantee the stealth Backstab opener before entering the repeatable mini-cycle.
                    Spell.Cast("Backstab", ret => Core.Player.IsStealthed),

                    //Poison upkeep gives Volatile Substance a detonator.
                    Spell.Cast("Corrosive Dart",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Corrosive Dart") || Core.Player.Target.DebuffTimeLeft("Corrosive Dart") <= 2)),

                    //Volatile Substance arms for three seconds. Crippling Slice occupies the intervening
                    //GCD before Backstab detonates it; missing optional abilities simply fall through.
                    Spell.Cast("Volatile Substance", ret => !HoldForOpener),
                    Spell.Cast("Crippling Slice",
                        ret => !HoldForOpener && Core.Player.Target.HasMyDebuff("Volatile Substance")),
                    Spell.Cast("Backstab",
                        ret => !HoldForOpener && Core.Player.Target.HasMyDebuff("Volatile Substance")),

                    //Spend Tactical Advantage, then use Backstab normally outside the mini-cycle.
                    Spell.Cast("Laceration", ret => !HoldForOpener && Core.Player.HasBuff("Tactical Advantage")),
                    Spell.Cast("Backstab", ret => !HoldForOpener),

                    //Build Tactical Advantage (Shiv is the pre-Veiled Strike low level generator)
                    Spell.Cast("Veiled Strike", ret => !HoldForOpener && Core.Player.BuffCount("Tactical Advantage") < 2),
                    Spell.Cast("Shiv", ret => !HoldForOpener && Core.Player.BuffCount("Tactical Advantage") < 2),

                    //Cheap fillers
                    Spell.Cast("Crippling Slice", ret => !HoldForOpener),
                    Spell.Cast("Overload Shot", ret => !HoldForOpener && Core.Player.EnergyPercent >= 85 && !Core.Player.HasBuff("Tactical Advantage")),

                    //Never stall
                    Spell.Cast("Rifle Shot", ret => !HoldForOpener)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe && !HoldForOpener,
                    new PrioritySelector(
                        Spell.Cast("Toxic Haze", ret => Core.Player.HasBuff("Tactical Advantage")),
                        Spell.Cast("Noxious Knives"),
                        Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent >= 60)
                    ));
            }
        }
    }
}
