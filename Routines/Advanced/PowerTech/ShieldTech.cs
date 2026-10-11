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
    // 7.x Shield Tech (tank). Core.Player.EnergyPercent is resource REMAINING (100 = no heat, 0 = overheated),
    // so "low EnergyPercent" == "high heat" == conserve.
    /// <summary>
    ///     Powertech Shield Tech (tank) rotation: Rocket Punch / Rail Shot build Heat Screens
    ///     for off-GCD Heat Blasts; guards the companion while solo.
    /// </summary>
    public class ShieldTech : RotationBase
    {
        // Firestorm is self-cast and lands through an AoE Cone target override (abl.bounty_hunter.firestorm):
        // Direction Forward, Distance 10.1 m, Angle 45 (read as the full cone width).
        private const float FirestormReach = 1.0f;
        private const float FirestormArc = 45f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.ShieldTech;

        public override string Name => "Powertech Shield Tech";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Hunter's Boon"),
                    Spell.Cast("Guard", on => Core.Player.Companion, ret => Core.Player.Companion != null && !Core.Player.Companion.IsDead && !Core.Player.Companion.HasBuff("Guard"))
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Determination", ret => Core.Player.IsStunned),

                    //Interrupt lives here so a heat-starved rotation can never swallow it
                    Spell.Cast("Quell", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Heat Blast is off-GCD: dumps 3 Heat Screens for damage, an absorb buff and 10 vented heat
                    Spell.Cast("Heat Blast", ret => Core.Player.BuffCount("Heat Screen") >= 3 || Core.Player.Level < 50),

                    //Defensives
                    Spell.Buff("Energy Shield", ret => Core.Player.HealthPercent <= 60),
                    //Each Powertech discipline has its own "special"-slot yield. Shield Tech's is
                    //"Energy Yield" (abl.bounty_hunter.skill.shield_tech.mods.special.energy_yield;
                    //live-verified: 60s cd, off-GCD). "Power Yield" is Advanced Prototype's and
                    //"Thermal Yield" is Pyrotech's -- neither is granted here.
                    Spell.Buff("Energy Yield", ret => Core.Player.InCombat && Core.Player.HealthPercent <= 70),
                    //Oil Slick is an 8 m sphere around us, not the target.
                    Spell.CastOnGround("Oil Slick", ret => Core.Player.HealthPercent <= 75 && Core.Player.Target.Distance <= 0.8f),
                    Spell.Buff("Kolto Overload", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Heat: 7.0 folded Thermal Sensor Override into Vent Heat
                    Spell.Cast("Vent Heat", ret => Core.Player.EnergyPercent <= 40),

                    //Offensive cooldowns
                    Spell.Cast("Explosive Fuel", ret => Core.Player.InCombat && Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.InCombat && !Core.Player.HasBuff("Shoulder Cannon"))
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //(No charge here: Jet Charge is an Advanced Prototype / Pyrotech ability-tree pick.
                    // Shield Tech's tier-2 slot is Extraction Plan instead, so it has no gap-closer.)

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Overheated -- free / procced casts only until heat bleeds off
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            //Firestorm is a 10 m forward cone from us; the engine never range-checks it.
                            Spell.Cast("Firestorm", ret => Core.Player.HasBuff("Flame Engine") && Targeting.InFrontalCone(Core.Player.Target, FirestormReach, FirestormArc)),
                            Spell.Cast("Flame Burst", ret => Core.Player.HasBuff("Flame Surge")),
                            Spell.Cast("Rapid Shots")
                            )),

                    //Rotation: Rocket Punch and Rail Shot are the Heat Screen generators, so they lead
                    Spell.Cast("Rocket Punch"),
                    Spell.Cast("Rail Shot"),
                    //Firestorm is a 10 m forward cone from us; the engine never range-checks it.
                    Spell.Cast("Firestorm",
                        ret => (Core.Player.HasBuff("Flame Engine") || Core.Player.Level < 50) && Targeting.InFrontalCone(Core.Player.Target, FirestormReach, FirestormArc)),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),

                    //Fillers
                    Spell.Cast("Flame Burst", ret => Core.Player.HasBuff("Flame Surge")),
                    Spell.Cast("Flame Burst", ret => Core.Player.EnergyPercent >= 55),
                    Spell.Cast("Rapid Shots")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new PrioritySelector(
                    new Decorator(ret => Targeting.ShouldAoe,
                        new PrioritySelector(
                            //Deadly Onslaught is ground-placed at the target: 10 m max range, 5 m sphere.
                            Spell.CastOnGround("Deadly Onslaught", ret => Core.Player.Target.Distance <= 1.0f && Targeting.ShouldTargetAoe(0.5f))
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            //Forward cone, not a point-blank AoE: enemies around us only count when they stand inside it.
                            Spell.Cast("Firestorm", ret => Targeting.ShouldFrontalConeAoe(FirestormReach, FirestormArc)),
                            //Shatter Slug hits an 8 m sphere around the target.
                            Spell.Cast("Shatter Slug", ret => Targeting.ShouldTargetAoe(0.8f)),
                            //Flame Sweep is a 5 m sphere around us, not the target.
                            Spell.Cast("Flame Sweep", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldPointBlankAoe(0.5f))
                            )));
            }
        }
    }
}
