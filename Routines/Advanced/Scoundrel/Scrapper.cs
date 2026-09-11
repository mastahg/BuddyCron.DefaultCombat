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
    ///     7.x Scoundrel Scrapper (melee burst DPS) rotation. Holds the priority while stealthed
    ///     so the opener is always the stealth Back Blast.
    /// </summary>
    public class Scrapper : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Scrapper;

        public override string Name => "Scoundrel Scrapper";

        /// <summary>
        ///     While stealthed with Back Blast available we hold everything else so the opener is
        ///     always the (auto-crit, Upper-Hand-generating, Flechette Round applying) stealth Back
        ///     Blast. If Back Blast is on cooldown we drop the hold rather than stall out of stealth.
        /// </summary>
        private bool HoldForOpener => Core.Player.IsStealthed && Core.Player.Target != null &&
                                             AbilityManager.CanCast("Back Blast", Core.Player.Target).Success;

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
                    Spell.Buff("Defense Screen", ret => Core.Player.HealthPercent <= 75),
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
                    //Gap closer (ability tree choice, lvl 73)
                    Spell.Cast("Trick Move", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance > .4f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Energy floor - free filler while Cool Head is down
                    Spell.Cast("Flurry of Bolts",
                        ret => Core.Player.EnergyPercent < 60 && !AbilityManager.CanCast("Cool Head", Core.Player).Success),

                    //Guarantee the stealth Back Blast opener before entering the repeatable mini-cycle.
                    Spell.Cast("Back Blast", ret => Core.Player.IsStealthed),

                    //Bleed upkeep gives Blood Boiler a detonator and remains the low-level fallback.
                    Spell.Cast("Vital Shot",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Vital Shot") || Core.Player.Target.DebuffTimeLeft("Vital Shot") <= 2)),

                    //Blood Boiler arms for three seconds. Shank Shot occupies the intervening GCD
                    //before Back Blast detonates it; missing optional abilities simply fall through.
                    Spell.Cast("Blood Boiler", ret => !HoldForOpener),
                    Spell.Cast("Shank Shot",
                        ret => !HoldForOpener && Core.Player.Target.HasMyDebuff("Blood Boiler")),
                    Spell.Cast("Back Blast",
                        ret => !HoldForOpener && Core.Player.Target.HasMyDebuff("Blood Boiler")),

                    //Spend Upper Hand, then use Back Blast normally outside the mini-cycle.
                    Spell.Cast("Sucker Punch", ret => !HoldForOpener && Core.Player.HasBuff("Upper Hand")),
                    Spell.Cast("Back Blast", ret => !HoldForOpener),

                    //Build Upper Hand (Blaster Whip is the pre-Bludgeon low level generator)
                    Spell.Cast("Bludgeon", ret => !HoldForOpener && Core.Player.BuffCount("Upper Hand") < 2),
                    Spell.Cast("Blaster Whip", ret => !HoldForOpener && Core.Player.BuffCount("Upper Hand") < 2),

                    //Cheap fillers
                    Spell.Cast("Shank Shot", ret => !HoldForOpener),
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
                        Spell.Cast("Bushwhack", ret => Core.Player.HasBuff("Upper Hand")),
                        Spell.Cast("Lacerating Blast"),
                        Spell.Cast("Thermal Grenade", ret => Core.Player.EnergyPercent >= 60)
                    ));
            }
        }
    }
}
