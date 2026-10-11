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
    ///     7.x Vanguard Shield Specialist (tank) rotation, Republic mirror of Shield Tech.
    ///     Cell convention: Core.Player.EnergyPercent is the REMAINING resource (100 = full energy cells,
    ///     0 = empty), so a low value means conserve.
    /// </summary>
    public class ShieldSpecialist : RotationBase
    {
        // Ion Storm is self-cast and lands through an AoE Cone target override (abl.trooper.ion_storm):
        // Direction Forward, Distance 10.1 m, Angle 45 (read as the full cone width).
        private const float IonStormReach = 1.0f;
        private const float IonStormArc = 45f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.ShieldSpecialist;

        public override string Name => "Vanguard Shield Specialist";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Fortification"),
                    Spell.Cast("Guard", on => Core.Player.Companion, ret => Core.Player.Companion != null && !Core.Player.Companion.IsDead && !Core.Player.Companion.HasBuff("Guard"))
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Tenacity", ret => Core.Player.IsStunned),

                    //Interrupt lives here so a cell-starved rotation can never swallow it
                    Spell.Cast("Riot Strike", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Energy Blast is off-GCD: dumps 3 Power Screens for damage, an absorb buff and cells
                    Spell.Cast("Energy Blast", ret => Core.Player.BuffCount("Power Screen") >= 3 || Core.Player.Level < 50),

                    //Defensives
                    Spell.Buff("Reactive Shield", ret => Core.Player.HealthPercent <= 60),
                    //(Power Yield is a Powertech/Advanced Prototype ability -- no Vanguard discipline
                    // grants it. Shield Specialist's "special"-slot pick is Infused Kolto Packs.)
                    //Riot Gas is an 8 m sphere around us, not the target.
                    Spell.Cast("Riot Gas", ret => Core.Player.HealthPercent <= 75 && Core.Player.Target.Distance <= 0.8f),
                    Spell.Buff("Adrenaline Rush", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Cells: 7.0 folded Reserve Powercell into Recharge Cells
                    Spell.Cast("Recharge Cells", ret => Core.Player.EnergyPercent <= 40),

                    //Offensive cooldowns
                    Spell.Cast("Battle Focus", ret => Core.Player.InCombat && Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.InCombat && !Core.Player.HasBuff("Shoulder Cannon"))
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //(No charge here: Storm is a Tactics / Plasmatech ability-tree pick. Shield
                    // Specialist's tier-2 slot is Extraction Plan instead, so it has no gap-closer.)

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Low cells -- free / procced casts only until energy regenerates
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            //Ion Storm is a 10 m forward cone from us; the engine never range-checks it.
                            Spell.Cast("Ion Storm", ret => Core.Player.HasBuff("Pulse Engine") && Targeting.InFrontalCone(Core.Player.Target, IonStormReach, IonStormArc)),
                            Spell.Cast("Ion Pulse", ret => Core.Player.HasBuff("Static Surge")),
                            Spell.Cast("Hammer Shot")
                            )),

                    //Rotation: Stockstrike and High Impact Bolt are the Power Screen generators,
                    //so they lead -- Energy Blast is fired off-GCD from Cooldowns
                    Spell.Cast("Stockstrike"),
                    Spell.Cast("High Impact Bolt"),
                    //Ion Storm is a 10 m forward cone from us; the engine never range-checks it.
                    Spell.Cast("Ion Storm",
                        ret => (Core.Player.HasBuff("Pulse Engine") || Core.Player.Level < 50) && Targeting.InFrontalCone(Core.Player.Target, IonStormReach, IonStormArc)),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),

                    //Fillers
                    Spell.Cast("Ion Pulse", ret => Core.Player.HasBuff("Static Surge")),
                    Spell.Cast("Ion Pulse", ret => Core.Player.EnergyPercent >= 55),
                    Spell.Cast("Hammer Shot")
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
                            //Artillery Blitz is ground-placed at the target: 10 m max range, 5 m sphere.
                            Spell.CastOnGround("Artillery Blitz", ret => Core.Player.Target.Distance <= 1.0f && Targeting.ShouldTargetAoe(0.5f))
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            //Forward cone, not a point-blank AoE: enemies around us only count when they stand inside it.
                            Spell.Cast("Ion Storm", ret => Targeting.ShouldFrontalConeAoe(IonStormReach, IonStormArc)),
                            //Flak Shell hits an 8 m sphere around the target.
                            Spell.Cast("Flak Shell", ret => Targeting.ShouldTargetAoe(0.8f)),
                            //Explosive Surge is a 5 m sphere around us, not the target.
                            Spell.Cast("Explosive Surge", ret => Core.Player.EnergyPercent >= 50 && Targeting.ShouldPointBlankAoe(0.5f))
                            )));
            }
        }
    }
}
