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
    ///     Assassin Hatred (DoT melee dps) rotation: Death Field on cooldown, keeps the
    ///     Creeping Terror / Discharge DoTs rolling and spends Raze procs on free Eradicates.
    /// </summary>
    public class Hatred : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Hatred;

        public override string Name => "Assassin Hatred";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Mark of Power"),
                    //Re-stealth out of combat: the opener is Phantom Stride (free Raze) into the DoTs
					Spell.Buff("Stealth", ret => !Rest.KeepResting() && !RotationRuntime.MovementDisabled && !Core.Player.IsMounted)
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Unbreakable Will", ret => Core.Player.IsStunned),
                    Spell.Buff("Overcharge Saber", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Deflection", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Force Shroud", ret => Core.Player.HealthPercent <= 50),
                    Spell.Buff("Recklessness", ret => Core.Player.InCombat),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //Phantom Stride is the stealth opener and also grants Raze
                    Spell.Cast("Phantom Stride", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),
                    Spell.Cast("Force Speed", ret => CombatHotkeys.EnableCharge && Core.Player.IsMoving && Core.Player.Target.EdgeDistance > 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Interrupts
                    Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Electrocute", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Rotation
                    //Raze finishes Eradicate's cooldown and makes it free - always spend it
                    Spell.Cast("Eradicate", ret => Core.Player.HasBuff("Raze")),
                    //Death Field on cooldown: it applies Overwhelmed (Mental) and boosts our DoT damage
                    Spell.CastOnGround("Death Field"),
                    //Keep both 18s DoTs rolling
                    Spell.Cast("Creeping Terror",
                        ret => !Core.Player.Target.HasMyDebuff("Creeping Terror") || Core.Player.Target.DebuffTimeLeft("Creeping Terror") <= 3),
                    Spell.Cast("Discharge",
                        ret => !Core.Player.Target.HasMyDebuff("Discharge") || Core.Player.Target.DebuffTimeLeft("Discharge") <= 3),
                    Spell.Cast("Assassinate", ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Reaper's Rush")),
                    Spell.Cast("Leeching Strike", ret => Core.Player.ForcePercent >= 30),
                    //Eradicate off cooldown even without a Raze proc (low levels never proc Raze)
                    Spell.Cast("Eradicate", ret => Core.Player.ForcePercent >= 50),
                    Spell.Cast("Thrash", ret => Core.Player.ForcePercent >= 45),
                    Spell.Cast("Saber Strike")

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
                            Spell.CastOnGround("Death Field"),
                            Spell.Cast("Discharge", ret => !Core.Player.Target.HasMyDebuff("Discharge")),
                            Spell.Cast("Creeping Terror", ret => !Core.Player.Target.HasMyDebuff("Creeping Terror")),
							Spell.Cast("Severing Slash"))),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        //Lacerate is the AoE filler and spreads the DoTs
                        Spell.Cast("Lacerate", ret => Core.Player.ForcePercent >= 40))
                    );
            }
        }
    }
}
