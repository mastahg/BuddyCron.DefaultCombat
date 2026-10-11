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
    // 7.x Gunnery Commando (Republic mirror of Arsenal Mercenary).
    // Cell convention: Core.Player.EnergyPercent is the REMAINING resource (100 = full Energy Cells,
    // 0 = empty), so "starved" == low EnergyPercent. Hammer Shot is the free filler that lets
    // cells regenerate.
    /// <summary>
    ///     Commando Gunnery (ranged burst dps) rotation: Vortex Bolt / Grav Round feed
    ///     High Impact Bolt, Demolition Round and Boltstorm.
    /// </summary>
    public class Gunnery : RotationBase
    {
        // Mortar Volley and Hail of Bolts are Ground abilities: 5 m sphere at the target point, 30 m range.
        private const float GroundAoeRadius = 0.5f;
        private const float GroundAoeRange = 3.0f;
        // Plasma Grenade and Sticky Grenade explode on the target: 8 m sphere around it.
        private const float GrenadeBlastRadius = 0.8f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Gunnery;

        public override string Name => "Commando Gunnery";

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
                    //Gunnery's Supercharge spender is named "High Velocity Supercharged Cell"
                    //(abl.trooper.skill.gunnery.supercharged_cell_high_velocity). Plain "Supercharged
                    //Cell" is the Assault Specialist one and never matches here.
                    Spell.Buff("High Velocity Supercharged Cell", ret => Core.Player.InCombat && Core.Player.BuffCount("Supercharge") >= 10),
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

                    //Rotation
                    Spell.Cast("Vortex Bolt"),                                          //highest-damage ability, grants Grav Primer (Buff 12s)
                    Spell.Cast("High Impact Bolt", ret => Core.Player.BuffCount("Charged Barrel") >= 5 || Core.Player.Level < 40),   //Buff, caps at 5
                    Spell.Cast("Demolition Round", ret => Core.Player.Target.HasDebuff("Gravity Vortex") || Core.Player.Level < 40), //Debuff 45s, from Grav Round
                    Spell.Cast("Electro Net", ret => Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Boltstorm", ret => Core.Player.EnergyPercent >= 40),             //cooldown finished by the Curtain of Fire proc
                    Spell.Cast("Grav Round", ret => Core.Player.HasBuff("Grav Primer") || Core.Player.EnergyPercent >= 50),

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
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Mortar Volley", ret => Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Explodes on the target: 8 m sphere around it.
                        Spell.Cast("Plasma Grenade", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldTargetAoe(GrenadeBlastRadius)),
                        //Sticks to the target and explodes in an 8 m sphere around it.
                        Spell.Cast("Sticky Grenade", ret => Targeting.ShouldTargetAoe(GrenadeBlastRadius)),   //ability-tree choice
                        //Ground: 5 m sphere around the target point, up to 30 m away.
                        Spell.CastOnGround("Hail of Bolts", ret => Core.Player.EnergyPercent >= 40 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
