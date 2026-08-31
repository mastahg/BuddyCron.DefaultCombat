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

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     Guardian Vigilance (DoT melee dps) rotation: Plasma Brand / Blade Barrage / Overhead
    ///     Slash on cooldown keep the burns rolling; Blade Storm autocrits at 2 Force Rush.
    /// </summary>
    public class Vigilance : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Vigilance;

        public override string Name => "Guardian Vigilance";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Force Might")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Resolute", ret => Core.Player.IsStunned),

                    //Offensive
                    Spell.Buff("Force Clarity", ret => Core.Player.Target.BossOrGreater()),

                    //Focus generation. Burning Focus is a passive that upgrades Combat Focus to also
                    //detonate burns — not a castable ability. Combat Focus is the button.
                    Spell.Cast("Combat Focus", ret => Core.Player.ActionPoints <= 6),

                    //Defensives
                    Spell.Buff("Saber Reflect", ret => Core.Player.HealthPercent <= 90),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 50),
                    Spell.Buff("Focused Defense", ret => Core.Player.HealthPercent < 70),
                    Spell.Buff("Enure", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Force Leap", ret => CombatHotkeys.EnableCharge && Core.Player.Target.Distance >= 1f),
                    Spell.Cast("Saber Throw", ret => Core.Player.Target.Distance > .4f && Core.Player.Target.Distance <= 3f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupts
                    Spell.Cast("Force Kick", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Alternate the repeating Overhead Slash / Blade Storm / Vigilant Thrust core with
                    //priority slots. Plasma Brand takes the first open slot and resets Blade Barrage;
                    //Blade Barrage is then consumed in a later slot while multiple burns are active.
                    Spell.Cast("Leaping Strike"), // replaces Overhead Slash when selected
                    Spell.Cast("Overhead Slash"),
                    Spell.Cast("Plasma Brand"),
                    Spell.Cast("Blade Storm", ret => Core.Player.BuffCount("Force Rush") >= 2 || Core.Player.Level < 40),
                    Spell.Cast("Blade Barrage"),
                    Spell.Cast("Vigilant Thrust", ret => Core.Player.Target.Distance <= 0.5f),

                    //Whirling Blade replaces Dispatch for Vigilance; Keening makes it free and usable
                    //at any health. Dispatch is the pre-replacement (low level) fallback.
                    Spell.Cast("Whirling Blade", ret => Core.Player.HasBuff("Keening") || Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Dispatch", ret => Core.Player.Target.HealthPercent <= 30),

                    //Safety net: never let Blade Storm rot if the Force Rush proc never lands.
                    Spell.Cast("Blade Storm"),

                    //Fillers
                    Spell.Cast("Sundering Strike", ret => Core.Player.ActionPoints <= 5),
                    Spell.Cast("Riposte"),
                    Spell.Cast("Slash", ret => Core.Player.ActionPoints >= 6),
                    Spell.Cast("Strike")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldPbaoe,
                    new PrioritySelector(
                        //Get the burns up first, then spread them with Vigilant Thrust
                        Spell.Cast("Leaping Strike"),
                        Spell.Cast("Overhead Slash"),
                        Spell.Cast("Plasma Brand"),
                        Spell.Cast("Blade Storm"),
                        Spell.Cast("Vigilant Thrust", ret => Core.Player.Target.Distance <= 0.5f),
                        Spell.Cast("Force Sweep", ret => Core.Player.Target.Distance <= 0.5f),
                        Spell.Cast("Cyclone Slash", ret => Core.Player.Target.Distance <= 0.5f),
                        Spell.Cast("Blade Barrage")
                        ));
            }
        }
    }
}
