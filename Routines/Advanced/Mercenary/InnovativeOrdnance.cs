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
    // 7.x Innovative Ordnance Mercenary.
    // Heat convention: Core.Player.EnergyPercent is the REMAINING resource (100 = stone cold, 0 = fully
    // overheated), so "hot" == low EnergyPercent. Rapid Shots is the free heat dump.
    /// <summary>
    ///     Mercenary Innovative Ordnance (DoT ranged dps) rotation: keeps the Incendiary Missile /
    ///     Serrated Shot DoTs at full uptime, with Mag Shot and Unload between refreshes.
    /// </summary>
    public class InnovativeOrdnance : RotationBase
    {
        // Death from Above and Sweeping Blasters are Ground abilities: 5 m sphere at the target point, 30 m range.
        private const float GroundAoeRadius = 0.5f;
        private const float GroundAoeRange = 3.0f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.InnovativeOrdnance;

        public override string Name => "Mercenary Innovative Ordnance";

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
                    Spell.Buff("Supercharged Gas", ret => Core.Player.InCombat && Core.Player.BuffCount("Supercharge") >= 10),
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

                    //DoT upkeep (the whole point of the spec). Debuff names verified against the ability data:
                    //abl.bounty_hunter.skill.firebug.incendiary_missile      -> "Burning (Incendiary Missile)" (Debuff, 15s)
                    //abl.bounty_hunter.skill.innovative_ordnance.serrated_shot -> "Bleeding" (Debuff, 15s)
                    Spell.DoT("Incendiary Missile", "Burning (Incendiary Missile)", 12000),
                    Spell.DoT("Serrated Shot", "Bleeding", 12000),

                    //Rotation
                    Spell.Cast("Thermal Detonator"),                                     //ability-tree choice (~39)
                    Spell.Cast("Mag Shot"),                                              //free/reset by the Innovative Particle Accelerator proc
                    Spell.Cast("Electro Net", ret => Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Unload"),
                    Spell.Cast("Power Shot", ret => Core.Player.HasBuff("Speed to Burn")),        //free instant
                    Spell.Cast("Power Shot", ret => Core.Player.EnergyPercent >= 55),

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
                        //Explodes on the target: 8 m sphere around it.
                        Spell.Cast("Fusion Missile", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldTargetAoe(0.8f)),
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Death from Above", ret => Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Sweeping Blasters", ret => Core.Player.EnergyPercent >= 40 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)))
                    );
            }
        }
    }
}
