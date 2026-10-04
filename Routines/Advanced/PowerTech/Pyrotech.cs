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
    // 7.x Pyrotech. Core.Player.EnergyPercent is resource REMAINING (100 = no heat, 0 = overheated),
    // so "low EnergyPercent" == "high heat" == conserve.
    /// <summary>
    ///     Powertech Pyrotech (DoT melee dps) rotation: keeps the burns up so Rail Shot stays
    ///     usable and spends Superheated Flamethrower stacks on Searing Wave.
    /// </summary>
    public class Pyrotech : RotationBase
    {
        // Searing Wave's damage lands through an AoE Cone target override (abl.bounty_hunter.searing_wave
        // effect 1): Direction Forward, Distance 10.1 m, Angle 45. Angle is read as the full cone width,
        // the same full-angle convention ablTargetArc uses (270/360) — the conservative reading. Reach
        // stays just under 10.1 m so a pulse of movement cannot carry a counted enemy out.
        private const float SearingWaveReach = 1.0f;
        private const float SearingWaveArc = 45f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.FirebugPyrotech;

        public override string Name => "Powertech Pyrotech";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Hunter's Boon")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Determination", ret => Core.Player.IsStunned),

                    //Interrupt lives here so a heat-starved rotation can never swallow it
                    Spell.Cast("Quell", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Defensives
                    Spell.Buff("Energy Shield", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Kolto Overload", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Heat: 7.0 folded Thermal Sensor Override into Vent Heat
                    Spell.Cast("Vent Heat", ret => Core.Player.EnergyPercent <= 40),

                    //Offensive cooldowns
                    Spell.Cast("Explosive Fuel", ret => Core.Player.InCombat && Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.InCombat && !Core.Player.HasBuff("Shoulder Cannon")),

                    //DoT upkeep -- Rail Shot needs the target burning
                    Spell.DoT("Incendiary Missile", "Burning (Incendiary Missile)"),
                    Spell.DoT("Scorch", "Scorch")
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Jet Charge", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Overheated -- free casts only until heat bleeds off
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            Spell.Cast("Flame Burst", ret => Core.Player.HasBuff("Flame Barrage")),
                            Spell.Cast("Rapid Shots")
                            )),

                    //Flaming Fist is the defining short-cooldown strike and helps establish the proc
                    //state consumed by Searing Wave, Immolate, and Rail Shot.
                    Spell.Cast("Flaming Fist"),
                    //Searing Wave is self-cast, so the engine never range-checks the target: keep it to its 10 m reach
                    //and the target inside the forward cone.
                    Spell.Cast("Searing Wave", ret => (Core.Player.BuffCount("Superheated Flamethrower") >= 2 || Core.Player.Level < 50) && Core.Player.Target.EdgeDistance <= 1f && Targeting.InFrontalCone(Core.Player.Target, SearingWaveReach, SearingWaveArc)),
                    Spell.Cast("Immolate", ret => Core.Player.HasBuff("Consuming Flames") || Core.Player.Level < 50),
                    //Rail Shot is only usable on a target suffering periodic damage (or CC'd) unless the
                    //caster has Prototype Rail (Advanced Prototype only). Pyrotech's two DoTs are the only
                    //burns we bring -- there is no aura literally named "Burning".
                    Spell.Cast("Rail Shot", ret => Core.Player.Target.HasMyDebuff("Burning (Incendiary Missile)") || Core.Player.Target.HasMyDebuff("Scorch")),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),

                    //Fallback so a missing proc-passive can never park Immolate
                    Spell.Cast("Immolate"),

                    //Fillers
                    Spell.Cast("Flame Burst", ret => Core.Player.HasBuff("Flame Barrage")),
                    Spell.Cast("Flame Burst", ret => Core.Player.EnergyPercent >= 65),
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
                            CombatMovement.CloseDistance(Distance.MeleeAoE),
                            Spell.DoT("Incendiary Missile", "Burning (Incendiary Missile)"),
                            Spell.DoT("Scorch", "Scorch"),
                            Spell.CastOnGround("Deadly Onslaught")
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            CombatMovement.CloseDistance(Distance.MeleeAoE),
                            //Forward cone, not a point-blank AoE: enemies around us only count when they stand inside it.
                            Spell.Cast("Searing Wave", ret => Core.Player.Target.EdgeDistance <= 1f && Targeting.ShouldFrontalConeAoe(SearingWaveReach, SearingWaveArc)),
                            Spell.Cast("Flame Sweep", ret => Core.Player.HasBuff("Flame Barrage")),
                            //(Shatter Slug is granted to Advanced Prototype and Shield Tech only -- not Pyrotech.)
                            Spell.Cast("Flame Sweep", ret => Core.Player.EnergyPercent >= 50)
                            )));
            }
        }
    }
}
