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
    // 7.x Arsenal Mercenary.
    // Heat convention: Core.Player.EnergyPercent is the REMAINING resource (100 = stone cold, 0 = fully
    // overheated), so "hot" == low EnergyPercent. Rapid Shots is the free heat dump.
    /// <summary>
    ///     Mercenary Arsenal (ranged burst dps) rotation: Priming Shot / Tracer Missile feed
    ///     Rail Shot, Heatseeker Missiles and Blazing Bolts.
    /// </summary>
    public class Arsenal : RotationBase
    {
        // Death from Above and Sweeping Blasters are Ground abilities: 5 m sphere at the target point, 30 m range.
        private const float GroundAoeRadius = 0.5f;
        private const float GroundAoeRange = 3.0f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Arsenal;

        public override string Name => "Mercenary Arsenal";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Hunter's Boon")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Determination", ret => Core.Player.IsStunned),

                    //Defensives -- these are what keep a leveling character alive
                    Spell.Buff("Energy Shield", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Chaff Flare", ret => Core.Player.HealthPercent <= 50),           //ability-tree choice (~43)
                    Spell.Buff("Kolto Overload", ret => Core.Player.HealthPercent <= 35),
                    Spell.Buff("Responsive Safeguards", ret => Core.Player.HealthPercent <= 30), //ability-tree choice (~68)
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Heat / offensive cooldowns
                    Spell.Buff("Vent Heat", ret => Core.Player.InCombat && Core.Player.EnergyPercent <= 40),
                    //Arsenal's Supercharge spender is named "High Velocity Supercharged Gas"
                    //(abl.bounty_hunter.skill.arsenal.supercharged_gas_high_velocity). Plain
                    //"Supercharged Gas" is the Innovative Ordnance one and never matches here.
                    Spell.Buff("High Velocity Supercharged Gas", ret => Core.Player.InCombat && Core.Player.BuffCount("Supercharge") >= 10),
                    Spell.Buff("Power Surge", ret => Core.Player.InCombat)
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

                    //Interrupt
                    Spell.Cast("Disabling Shot", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Emergency heat dump -- Rapid Shots is free and lets heat bleed off
                    Spell.Cast("Rapid Shots", ret => Core.Player.EnergyPercent <= 25),

                    //Rotation
                    Spell.Cast("Priming Shot"),
                    Spell.Cast("Tracer Missile", ret => Core.Player.HasBuff("Tracer Beacon")),   //Buff 12s, from Priming Shot
                    Spell.Cast("Rail Shot", ret => Core.Player.BuffCount("Tracer Lock") >= 5 || Core.Player.Level < 40),   //Buff, caps at 5
                    Spell.Cast("Heatseeker Missiles", ret => Core.Player.Target.HasDebuff("Heat Signature") || Core.Player.Level < 40),   //Debuff 45s, from Tracer Missile
                    Spell.Cast("Electro Net", ret => Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Blazing Bolts", ret => Core.Player.EnergyPercent >= 40),         //reset by the Barrage proc
                    Spell.Cast("Tracer Missile", ret => Core.Player.EnergyPercent >= 50),

                    //Never stall: free basic attack
                    Spell.Cast("Rapid Shots")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Death from Above", ret => Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Explodes on the target: 8 m sphere around it.
                        Spell.Cast("Fusion Missile", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldTargetAoe(0.8f)),
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Sweeping Blasters", ret => Core.Player.EnergyPercent >= 40 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                    ));
            }
        }
    }
}
