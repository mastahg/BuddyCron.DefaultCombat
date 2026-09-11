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
                    Spell.Cast("Riot Gas", ret => Core.Player.HealthPercent <= 75),
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
                            Spell.Cast("Ion Storm", ret => Core.Player.HasBuff("Pulse Engine") && Core.Player.Target.EdgeDistance <= 1f),
                            Spell.Cast("Ion Pulse", ret => Core.Player.HasBuff("Static Surge")),
                            Spell.Cast("Hammer Shot")
                            )),

                    //Rotation: Stockstrike and High Impact Bolt are the Power Screen generators,
                    //so they lead -- Energy Blast is fired off-GCD from Cooldowns
                    Spell.Cast("Stockstrike"),
                    Spell.Cast("High Impact Bolt"),
                    Spell.Cast("Ion Storm",
                        ret => (Core.Player.HasBuff("Pulse Engine") || Core.Player.Level < 50) && Core.Player.Target.EdgeDistance <= 1f),
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
                            Spell.CastOnGround("Artillery Blitz")
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            Spell.Cast("Ion Storm"),
                            Spell.Cast("Flak Shell"),
                            Spell.Cast("Explosive Surge", ret => Core.Player.EnergyPercent >= 50)
                            )));
            }
        }
    }
}
