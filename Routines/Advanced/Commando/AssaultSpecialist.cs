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
    // 7.x Assault Specialist Commando (Republic mirror of Innovative Ordnance Mercenary).
    // Cell convention: Core.Player.EnergyPercent is the REMAINING resource (100 = full Energy Cells,
    // 0 = empty), so "starved" == low EnergyPercent. Hammer Shot is the free filler that lets
    // cells regenerate.
    /// <summary>
    ///     Commando Assault Specialist (DoT ranged dps) rotation: keeps the Incendiary Round /
    ///     Serrated Bolt DoTs at full uptime, with Mag Bolt and Full Auto between refreshes.
    /// </summary>
    public class AssaultSpecialist : RotationBase
    {
        // Mortar Volley and Hail of Bolts are Ground abilities: 5 m sphere at the target point, 30 m range.
        private const float GroundAoeRadius = 0.5f;
        private const float GroundAoeRange = 3.0f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.AssaultSpecialist;

        public override string Name => "Commando Assault Specialist";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Fortification")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Tenacity", ret => Core.Player.IsStunned),

                    //Defensives -- these are what keep a leveling character alive
                    Spell.Buff("Reactive Shield", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Diversion", ret => Core.Player.HealthPercent <= 50),            //ability-tree choice
                    Spell.Buff("Adrenaline Rush", ret => Core.Player.HealthPercent <= 35),
                    Spell.Buff("Echoing Deterrence", ret => Core.Player.HealthPercent <= 30),   //ability-tree choice
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Cells / offensive cooldowns
                    Spell.Buff("Recharge Cells", ret => Core.Player.InCombat && Core.Player.EnergyPercent <= 40),
                    Spell.Buff("Supercharged Cell", ret => Core.Player.InCombat && Core.Player.BuffCount("Supercharge") >= 10),
                    Spell.Buff("Tech Override", ret => Core.Player.InCombat)
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

                    //Emergency cell dump -- Hammer Shot is free and lets cells regenerate
                    Spell.Cast("Hammer Shot", ret => Core.Player.EnergyPercent <= 25),

                    //DoT upkeep (the whole point of the spec). Debuff names verified against the ability data:
                    //abl.trooper.skill.plasmatech.incendiary_round        -> "Burning (Incendiary Round)" (Debuff, 15s)
                    //abl.trooper.skill.assault_specialist.serrated_bolt   -> "Bleeding" (Debuff, 15s)
                    Spell.DoT("Incendiary Round", "Burning (Incendiary Round)", 12000),
                    Spell.DoT("Serrated Bolt", "Bleeding", 12000),

                    //Rotation
                    Spell.Cast("Assault Plastique"),                                    //ability-tree choice
                    Spell.Cast("Mag Bolt"),                                             //free/reset by the Ionic Accelerator proc
                    Spell.Cast("Electro Net", ret => Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Full Auto"),
                    Spell.Cast("Explosive Round", ret => Core.Player.HasBuff("Hyper Assault Rounds")),   //free instant
                    Spell.Cast("Charged Bolts", ret => Core.Player.EnergyPercent >= 55),

                    //Never stall: free basic attack
                    Spell.Cast("Hammer Shot")
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
                        Spell.Cast("Plasma Grenade", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldTargetAoe(0.8f)),
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Mortar Volley", ret => Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        Spell.DoT("Incendiary Round", "Burning (Incendiary Round)", 12000),
                        Spell.DoT("Serrated Bolt", "Bleeding", 12000),
                        //Splashes standard/weak enemies within 5 m of the target.
                        Spell.Cast("Explosive Round", ret => Core.Player.HasBuff("Hyper Assault Rounds") && Targeting.ShouldTargetAoe(0.5f)),
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Hail of Bolts", ret => Core.Player.EnergyPercent >= 40 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
