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
    ///     7.x Scoundrel Ruffian (melee DoT DPS) rotation: keeps Vital Shot and Shrap Bomb
    ///     rolling and detonates them with Sanguinary Shot. Holds the priority while stealthed
    ///     so the opener is always Point Blank Shot.
    /// </summary>
    public class Ruffian : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Ruffian;

        public override string Name => "Scoundrel Ruffian";

        /// <summary>
        ///     While stealthed with Point Blank Shot available we hold everything else so the opener
        ///     is always the stealth Point Blank Shot (auto-crit, generates Upper Hand and Cut to the
        ///     Quick). If it is on cooldown we drop the hold rather than stall out of stealth.
        /// </summary>
        private bool HoldForOpener => Core.Player.IsStealthed && Core.Player.Target != null &&
                                             AbilityManager.CanCast("Point Blank Shot", Core.Player.Target).Success;

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Lucky Shots"),
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

                    //Raid buff - costs 1 Upper Hand
                    Spell.Buff("Stack the Deck", ret => CombatHotkeys.EnableRaidBuffs && Core.Player.HasBuff("Upper Hand")),

                    //Energy / Upper Hand economy
                    Spell.Cast("Cool Head", ret => Core.Player.EnergyPercent <= 45),
                    Spell.Cast("Pugnacity", ret => Core.Player.InCombat && Core.Player.BuffCount("Upper Hand") < 2),

                    //Ability tree choice (lvl 43) - resets Pugnacity / off-heals, save it for real fights
                    Spell.Buff("Hot Streak",
                        ret => Core.Player.InCombat && Core.Player.Target != null && Core.Player.Target.StrongOrGreater()),

                    //Defensives
                    Spell.Buff("Defense Screen", ret => Core.Player.HealthPercent <= 80),
                    Spell.Buff("Dodge", ret => Core.Player.HealthPercent <= 50),

                    //Off-heals (Scoundrels keep these in every spec)
                    Spell.Cast("Kolto Pack", on => Core.Player, ret => Core.Player.HealthPercent <= 40 && Core.Player.HasBuff("Upper Hand")),
                    Spell.HoT("Slow-release Medpac", on => Core.Player, 60),

                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //Gap closer (ability tree choice, lvl 68)
                    Spell.Cast("Trick Move", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance > .4f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Energy floor - free filler while Cool Head is down
                    Spell.Cast("Flurry of Bolts",
                        ret => Core.Player.EnergyPercent < 35 && !AbilityManager.CanCast("Cool Head", Core.Player).Success),

                    //Guarantee the stealth opener; the normal on-cooldown use sits after the DoTs.
                    Spell.Cast("Point Blank Shot", ret => Core.Player.IsStealthed),

                    //DoT upkeep - 100% uptime on both bleeds is the whole spec. Kept above the
                    //detonator so Sanguinary Shot always has something to hit.
                    Spell.Cast("Vital Shot",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Vital Shot") || Core.Player.Target.DebuffTimeLeft("Vital Shot") <= 3)),
                    Spell.Cast("Shrap Bomb",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Shrap Bomb") || Core.Player.Target.DebuffTimeLeft("Shrap Bomb") <= 3)),

                    //Open the bleed amplification window, use Point Blank Shot on cooldown, then
                    //spend Upper Hand on Bushwhack and Brutal Shots.
                    Spell.Cast("Sanguinary Shot", ret => !HoldForOpener),
                    Spell.Cast("Point Blank Shot", ret => !HoldForOpener),
                    Spell.Cast("Bushwhack", on => Core.Player,
                        ret => !HoldForOpener && Core.Player.HasBuff("Upper Hand")),

                    //Spend Upper Hand - primary filler. Unfair Advantage makes it free, but the buff
                    //only exists once the passive is trained, hence the plain Upper Hand check too.
                    Spell.Cast("Brutal Shots",
                        ret => !HoldForOpener && (Core.Player.HasBuff("Unfair Advantage") || Core.Player.HasBuff("Upper Hand"))),

                    //Build Upper Hand
                    Spell.Cast("Blaster Whip", ret => !HoldForOpener && Core.Player.BuffCount("Upper Hand") < 2),

                    //Cheap filler
                    Spell.Cast("Quick Shot", ret => !HoldForOpener && Core.Player.EnergyPercent >= 85 && !Core.Player.HasBuff("Upper Hand")),

                    //Never stall
                    Spell.Cast("Flurry of Bolts", ret => !HoldForOpener)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe && !HoldForOpener,
                    new PrioritySelector(
                        Spell.DoT("Shrap Bomb", "Shrap Bomb"),
                        Spell.Cast("Bushwhack", on => Core.Player,
                            ret => Core.Player.HasBuff("Upper Hand")),
                        Spell.Cast("Lacerating Blast"),
                        Spell.Cast("Thermal Grenade", ret => Core.Player.EnergyPercent >= 60)
                        ));
            }
        }
    }
}
