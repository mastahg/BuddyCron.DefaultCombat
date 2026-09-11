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
    // 7.x Advanced Prototype. Core.Player.EnergyPercent is resource REMAINING (100 = no heat, 0 = overheated),
    // so "low EnergyPercent" == "high heat" == conserve.
    /// <summary>
    ///     Powertech Advanced Prototype (melee dps) rotation: dumps Energy Lodes into Energy
    ///     Burst and keeps Rail Shot and the Retractable Blade bleed rolling.
    /// </summary>
    public class AdvancedPrototype : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.AdvancedPrototype;

        public override string Name => "Powertech Advanced Prototype";


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

                    //Defensive that also hands us Energy Lodes for a free Energy Burst. Each Powertech
                    //discipline gets its own "special"-slot yield: Advanced Prototype = "Power Yield"
                    //(abl.bounty_hunter.skill.advanced_prototype.mods.special.power_yield). "Energy Yield"
                    //is Shield Tech's and "Thermal Yield" is Pyrotech's -- neither is granted here.
                    Spell.Buff("Power Yield",
                        ret => Core.Player.InCombat && (Core.Player.HealthPercent <= 70 || Core.Player.Target.StrongOrGreater())),

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
                    Spell.Cast("Jet Charge", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Overheated -- Rail Shot is free off Prototype Particle Accelerator and vents on bleeding targets
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            Spell.Cast("Rail Shot"),
                            Spell.Cast("Rapid Shots")
                            )),

                    //Establish delayed damage and the bleed before Rail Shot. Consume a reset before
                    //another generator can overwrite it, then spend a full four-lode Energy Burst.
                    Spell.Cast("Thermal Detonator"),
                    Spell.DoT("Retractable Blade", "Bleeding (Retractable Blade)"),
                    Spell.Cast("Rail Shot", ret => Core.Player.HasBuff("Prototype Particle Accelerator")),
                    Spell.Cast("Energy Burst", ret => Core.Player.BuffCount("Energy Lode") >= 4),
                    Spell.Cast("Rail Shot"),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Rocket Punch"),

                    //Fillers
                    Spell.Cast("Magnetic Blast", ret => Core.Player.EnergyPercent >= 60),
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
                            Spell.CastOnGround("Deadly Onslaught")
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            Spell.Cast("Shatter Slug"),
                            Spell.Cast("Rail Shot"),
                            Spell.Cast("Flame Sweep", ret => Core.Player.EnergyPercent >= 50)
                            )));
            }
        }
    }
}
