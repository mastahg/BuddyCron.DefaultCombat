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
    ///     Gunslinger Dirty Fighting (DoT ranged dps) rotation: keeps Vital Shot and Shrap Bomb
    ///     at full uptime and channels Wounding Shots while both are ticking.
    /// </summary>
    public class DirtyFighting : RotationBase
    {
        // Bombing Run and Sweeping Gunfire: ground AoE, 8 m radius, 30 m max range.
        private const float GroundAoeRadius = 0.8f;
        private const float GroundAoeRange = 3.0f;
        // Shrap Bomb and Thermal Grenade splash 5 m around the target.
        private const float GrenadeSplashRadius = 0.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.DirtyFighting;

        public override string Name => "Gunslinger Dirty Fighting";

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

                    //Offensive
                    Spell.Cast("Smuggler's Luck", ret => Core.Player.Target.StrongOrGreater()),
                    //Ability tree choice (lvl 47) -- silently skipped when not chosen
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

                    //Cover -- Dirty Blast/Wounding Shots/Speed Shot want it and it is our energy regen
                    Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Rotation -- the dots are the whole spec, keep them at 100% uptime
                    Spell.DoT("Vital Shot", "Vital Shot"),
                    Spell.DoT("Shrap Bomb", "Shrap Bomb"),
                    //Hemorrhaging Blast makes every dot tick hit harder -- pair it with Wounding Shots
                    Spell.Cast("Hemorrhaging Blast",
                        ret => Core.Player.Target.HasMyDebuff("Vital Shot") || Core.Player.Target.HasMyDebuff("Shrap Bomb")),
                    //Wounding Shots only pays off while the dots are actually ticking. Shrap Bomb is a
                    //later pickup, so do not let its absence lock the channel out on a low level character.
                    Spell.Cast("Wounding Shots",
                        ret => Core.Player.Target.DebuffTimeLeft("Vital Shot") > 3
                               && (Core.Player.Target.DebuffTimeLeft("Shrap Bomb") > 3 || Core.Player.Level < 40)),
                    Spell.Cast("Quickdraw", ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Dirty Shot")),

                    //Low Energy -- Speed Shot is our cheap channel, use it before falling to Flurry of Bolts
                    Spell.Cast("Speed Shot", ret => Core.Player.EnergyPercent < 65),
                    Spell.Cast("Flurry of Bolts", ret => Core.Player.EnergyPercent < 50),

                    //Filler
                    Spell.Cast("Dirty Blast"),

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
                        //Shrap Bomb is our AoE dot and spreads via Extra Shrapnel -- land the dots first
                        Spell.DoT("Vital Shot", "Vital Shot"),
                        //Shrap Bomb only reaches 5 m around the target.
                        Spell.DoT("Shrap Bomb", "Shrap Bomb", 0, ret => Targeting.ShouldTargetAoe(GrenadeSplashRadius)),
                        //Bombing Run (7.0 rename of XS Freighter Flyby) is the lvl 68 tree choice -- skipped when not chosen
                        //Bombing Run is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Bombing Run", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 60 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Thermal Grenade only splashes 5 m around the target.
                        Spell.Cast("Thermal Grenade", ret => Core.Player.EnergyPercent > 50 && Targeting.ShouldTargetAoe(GrenadeSplashRadius)),
                        //Sweeping Gunfire is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Sweeping Gunfire", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 30 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
