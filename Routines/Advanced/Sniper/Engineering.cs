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
    ///     7.x Sniper Engineering (ranged DoT DPS) rotation, fought from cover and built around
    ///     Explosive Probe, Interrogation Probe and Plasma Probe. The most energy-hungry sniper
    ///     spec, so it bails to Rifle Shot early.
    /// </summary>
    public class Engineering : RotationBase
    {
        // Plasma Probe, Orbital Strike and Suppressive Fire are 8 m ground circles; Plasma Probe reaches 35 m, the others 30 m.
        private const float GroundAoeRadius = 0.8f;
        private const float GroundAoeRange = 3.0f;
        private const float PlasmaProbeRange = 3.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Engineering;

        public override string Name => "Sniper Engineering";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Coordination")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    //Survival
                    Spell.Buff("Escape", ret => Core.Player.IsStunned),
                    Spell.Buff("Shield Probe", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Evasion", ret => Core.Player.HealthPercent <= 40),
                    Spell.Buff("Entrench", ret => Core.Player.IsInCover() && Core.Player.HealthPercent <= 60),
                    //Ability tree choice (lvl 68) -- silently skipped when not chosen
                    Spell.Buff("Ballistic Shield", ret => Core.Player.HealthPercent <= 50),
                    //Resets defensive cooldowns when things go bad
                    Spell.Buff("Meticulous Preparation", ret => Core.Player.HealthPercent <= 25),

                    //Energy
                    Spell.Cast("Adrenaline Probe", ret => Core.Player.EnergyPercent <= 45),

                    //Offensive -- Laze Target is the autocrit cooldown; every Sniper discipline keeps it
                    Spell.Cast("Laze Target"),
                    //Ability tree choice (lvl 43) -- silently skipped when not chosen
                    Spell.Cast("Target Acquired", ret => Core.Player.Target.StrongOrGreater()),

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

                    //Cover -- Snipe/Series of Shots require it and it is our energy regen
                    Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Low Energy -- Engineering is the hungriest sniper spec, bail out early
                    new Decorator(ret => Core.Player.EnergyPercent < 45,
                        new PrioritySelector(
                            Spell.Cast("Rifle Shot")
                            )),

                    //Establish Interrogation Probe before the charge/channel cycle. Series of Shots
                    //refreshes it with the recommended tactical; DoT reapplies it when that refresh is absent.
                    Spell.DoT("Interrogation Probe", "Interrogation Probe"),
                    Spell.Cast("Explosive Probe"),
                    Spell.Cast("Series of Shots"),
                    //Plasma Probe is dropped at the target's feet from up to 35 m.
                    Spell.DoTGround("Plasma Probe", 9000, ret => Core.Player.Target.Distance <= PlasmaProbeRange),
                    Spell.Cast("EMP Discharge",
                        ret => Core.Player.Target.HasMyDebuff("Interrogation Probe") || Core.Player.Level < 40),
                    Spell.Cast("Takedown", ret => Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent > 60),
                    Spell.DoT("Corrosive Dart", "Corrosive Dart", 0, ret => Core.Player.EnergyPercent > 60),
                    //Orbital Strike is a ground circle at the target, cast from up to 30 m.
                    Spell.CastOnGround("Orbital Strike", ret => Core.Player.EnergyPercent > 75 && Core.Player.Target.Distance <= GroundAoeRange),
                    Spell.Cast("Snipe"),

                    //Never stall
                    Spell.Cast("Rifle Shot")
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
                        //Plasma Probe is an 8 m ground circle at the target, cast from up to 35 m.
                        Spell.DoTGround("Plasma Probe", 9000, ret => Core.Player.Target.Distance <= PlasmaProbeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Orbital Strike is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Orbital Strike", ret => Core.Player.EnergyPercent > 50 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Fragmentation Grenade only splashes 5 m around the target.
                        Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent > 50 && Targeting.ShouldTargetAoe(0.5f)),
                        //Suppressive Fire is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Suppressive Fire", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 30 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
