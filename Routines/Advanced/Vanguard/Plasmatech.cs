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
    ///     7.x Vanguard Plasmatech (DoT DPS) rotation, Republic mirror of Pyrotech.
    ///     Cell convention: Core.Player.EnergyPercent is the REMAINING resource (100 = full energy cells,
    ///     0 = empty), so a low value means conserve and fall back to Hammer Shot.
    /// </summary>
    public class Plasmatech : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Plasmatech;

        public override string Name => "Vanguard Plasmatech";

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

                    //Offensive cooldowns
                    Spell.Cast("Battle Focus", ret => Core.Player.InCombat && Core.Player.Target.StrongOrGreater()),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.InCombat && !Core.Player.HasBuff("Shoulder Cannon")),

                    //DoT upkeep -- High Impact Bolt needs the target burning
                    Spell.DoT("Incendiary Round", "Burning (Incendiary Round)"),
                    Spell.DoT("Plasmatize", "Plasmatize")
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

                    //Low cells -- free / procced casts only until energy regenerates
                    new Decorator(ret => Core.Player.EnergyPercent <= 40,
                        new PrioritySelector(
                            Spell.Cast("Ion Pulse", ret => Core.Player.HasBuff("Plasma Barrage")),
                            Spell.Cast("Hammer Shot")
                            )),

                    //Shockstrike is the defining short-cooldown strike and helps establish the proc
                    //state consumed by Ion Wave, Plasma Flare, and High Impact Bolt.
                    Spell.Cast("Shockstrike"),
                    Spell.Cast("Ion Wave", ret => Core.Player.BuffCount("Pulse Generator") >= 2 || Core.Player.Level < 50),
                    Spell.Cast("Plasma Flare", ret => Core.Player.HasBuff("Overcharged Plasma") || Core.Player.Level < 50),
                    //High Impact Bolt is only usable on a target suffering periodic damage (or CC'd) unless the
                    //caster has High Friction Bolts (Tactics only). Plasmatech's two DoTs are the only burns we
                    //bring -- there is no aura literally named "Burning".
                    Spell.Cast("High Impact Bolt",
                        ret => Core.Player.Target.HasMyDebuff("Burning (Incendiary Round)") ||
                               Core.Player.Target.HasMyDebuff("Plasmatize")),
                    Spell.Cast("Shoulder Cannon", ret => Core.Player.HasBuff("Shoulder Cannon") && Core.Player.Target.StrongOrGreater()),

                    //Fallback so a missing proc-passive can never park Plasma Flare
                    Spell.Cast("Plasma Flare"),

                    //Fillers
                    Spell.Cast("Ion Pulse", ret => Core.Player.HasBuff("Plasma Barrage")),
                    Spell.Cast("Ion Pulse", ret => Core.Player.EnergyPercent >= 65),
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
                            Spell.DoT("Incendiary Round", "Burning (Incendiary Round)"),
                            Spell.DoT("Plasmatize", "Plasmatize"),
                            Spell.CastOnGround("Artillery Blitz")
                            )),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        new PrioritySelector(
                            Spell.Cast("Ion Wave"),
                            Spell.Cast("Explosive Surge", ret => Core.Player.HasBuff("Plasma Barrage")),
                            //(Flak Shell is granted to Tactics and Shield Specialist only -- not Plasmatech.)
                            Spell.Cast("Explosive Surge", ret => Core.Player.EnergyPercent >= 50)
                            )));
            }
        }
    }
}
