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
    ///     7.x Vanguard Tactics (burst DPS) rotation, Republic mirror of Advanced Prototype.
    ///     Cell convention: Core.Player.EnergyPercent is the REMAINING resource (100 = full energy cells,
    ///     0 = empty), so a low value means conserve and fall back to Hammer Shot.
    /// </summary>
    public class Tactics : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Tactics;

        public override string Name => "Vanguard Tactics";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Fortification")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Tenacity", ret => Core.Player.IsStunned),

                    //Interrupt lives here so a cell-starved rotation can never swallow it
                    Spell.Cast("Riot Strike", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Defensives
                    Spell.Buff("Reactive Shield", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Adrenaline Rush", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Cells: 7.0 folded Reserve Powercell into Recharge Cells
                    Spell.Cast("Recharge Cells", ret => Core.Player.EnergyPercent <= 40),

                    //(Power Yield is a Powertech/Advanced Prototype ability -- no Vanguard discipline
                    // grants it. Tactics' "special"-slot pick is Balmorran Advanced Weaponry.)

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
                    Spell.Cast("Storm", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Low cells -- High Impact Bolt is free off Tactical Accelerator
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            Spell.Cast("High Impact Bolt", ret => Core.Player.HasBuff("Tactical Accelerator")),
                            Spell.Cast("Hammer Shot")
                            )),

                    //Establish delayed damage and the bleed before High Impact Bolt. Consume a reset
                    //before another generator can overwrite it, then spend a full four-lode Cell Burst.
                    Spell.Cast("Assault Plastique"),
                    //Gut's bleed is named just "Bleeding" (the Imperial mirror, Retractable Blade, uses
                    //"Bleeding (Retractable Blade)" -- the two are NOT named symmetrically). Gut has no
                    //cooldown, so a wrong name here re-casts it every GCD.
                    Spell.DoT("Gut", "Bleeding"),
                    Spell.Cast("High Impact Bolt", ret => Core.Player.HasBuff("Tactical Accelerator")),
                    Spell.Cast("Cell Burst", ret => Core.Player.BuffCount("Energy Lode") >= 4),
                    Spell.Cast("High Impact Bolt"),
                    Spell.Cast("Stockstrike"),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),

                    //Fillers
                    Spell.Cast("Tactical Surge", ret => Core.Player.EnergyPercent >= 55),
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
                            //Flak Shell spreads the Gut bleed to everything it hits
                            Spell.Cast("Flak Shell"),
                            Spell.Cast("Cell Burst", ret => Core.Player.BuffCount("Energy Lode") >= 4),
                            Spell.Cast("Explosive Surge", ret => Core.Player.EnergyPercent >= 50)
                        )));
            }
        }
    }
}
