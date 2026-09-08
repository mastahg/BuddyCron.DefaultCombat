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
    ///     Gunslinger Sharpshooter (ranged burst dps) rotation: fights from cover, weaving
    ///     Trickshot between Penetrating Rounds and Aimed Shot.
    /// </summary>
    public class Sharpshooter : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Sharpshooter;

        public override string Name => "Gunslinger Sharpshooter";


        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Lucky Shots")
        );


        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    //Survival
                    Spell.Buff("Escape", ret => Core.Player.IsStunned),
                    Spell.Buff("Defense Screen", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Dodge", ret => Core.Player.HealthPercent <= 40),
                    Spell.Buff("Hunker Down", ret => Core.Player.IsInCover() && Core.Player.HealthPercent <= 60),
                    //Ability tree choice (lvl 68) -- silently skipped when not chosen
                    Spell.Buff("Scrambling Field", ret => Core.Player.HealthPercent <= 50),
                    //Resets Defense Screen/Dodge/Hunker Down/Hightail It when things go bad
                    Spell.Buff("Bag of Tricks", ret => Core.Player.HealthPercent <= 25),

                    //Energy -- Cool Head at 45-50% keeps us in the top regen band
                    Spell.Cast("Cool Head", ret => Core.Player.EnergyPercent <= 50),
                    Spell.Cast("Burst Volley", ret => Core.Player.EnergyPercent <= 65),

                    //Offensive
                    Spell.Buff("Hunker Down", ret => Core.Player.Target.StrongOrGreater() && Core.Player.IsInCover()),
                    Spell.Buff("Smuggler's Luck"),
                    //Ability tree choice (lvl 43) -- silently skipped when not chosen
                    Spell.Cast("Illegal Mods", ret => Core.Player.Target.StrongOrGreater()),

                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }


        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),


                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Cover -- Charged Burst/Aimed Shot/Trickshot/Penetrating Rounds all need it,
                    //and Foxhole's energy regen only ticks while we are in it
                    Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Low Energy -- stay at/above the high regen band. Trickshot is cheap and its proc
                    //window is short, so it is the one thing we still fire while regenerating.
                    new Decorator(ret => Core.Player.EnergyPercent < 50,
                        new PrioritySelector(
                            Spell.Cast("Trickshot"),
                            Spell.Cast("Flurry of Bolts")
                            )),

                    //Consume any existing Trickshot window, then use Aimed Shot before the channel.
                    //The selector returns to Trickshot after either qualifying attack enables it.
                    Spell.Cast("Trickshot"),
                    //Charged Aim is a Sharpshooter passive; low level chars simply hard cast Aimed Shot.
                    Spell.Cast("Aimed Shot", ret => Core.Player.BuffCount("Charged Aim") >= 2 || Core.Player.Level < 30),
                    Spell.Cast("Penetrating Rounds"),
                    Spell.Cast("Quickdraw", ret => Core.Player.Target.HealthPercent <= 30),
                    //Filler dot -- only worth the energy on things that live long enough
                    Spell.DoT("Vital Shot", "Vital Shot", 0,
                        ret => Core.Player.Target.StrongOrGreater() && Core.Player.EnergyPercent > 60),
                    Spell.Cast("Charged Burst"),

                    //Never stall
                    Spell.Cast("Flurry of Bolts")
                    );
            }
        }


        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),
                        //Bombing Run (7.0 rename of XS Freighter Flyby) is the lvl 68 tree choice -- skipped when not chosen
                        Spell.CastOnGround("Bombing Run", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 60),
                        Spell.Cast("Thermal Grenade", ret => Core.Player.EnergyPercent > 50),
                        Spell.CastOnGround("Sweeping Gunfire", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 30)
                        ));
            }
        }
    }
}
