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
    ///     7.x Shadow Serenity (melee DoT DPS) rotation: keeps Sever Force and Force Breach
    ///     rolling and spends the Force Strike / Crush Spirit procs.
    /// </summary>
    public class Serenity : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Serenity;

        public override string Name => "Shadow Serenity";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Force Valor"),
                    //Re-stealth out of combat: the opener is Shadow Stride (free Squelch) into the DoTs
					Spell.Buff("Stealth", ret => !Rest.KeepResting() && !RotationRuntime.MovementDisabled && !Core.Player.IsMounted)
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Force of Will", ret => Core.Player.IsStunned),
                    Spell.Buff("Battle Readiness", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Deflection", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Resilience", ret => Core.Player.HealthPercent <= 50),
                    Spell.Buff("Force Potency", ret => Core.Player.InCombat),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //Shadow Stride is the stealth opener and also grants Force Strike
                    Spell.Cast("Shadow Stride", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),
                    Spell.Cast("Force Speed", ret => CombatHotkeys.EnableCharge && Core.Player.IsMoving && Core.Player.Target.EdgeDistance > 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Interrupts
                    Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Force Stun", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Rotation
                    //Force Strike finishes Squelch's cooldown and makes it free - always spend it
                    Spell.Cast("Squelch", ret => Core.Player.HasBuff("Force Strike")),
                    //Force in Balance on cooldown: it applies Overwhelmed (Mental) and boosts our DoT damage
                    Spell.CastOnGround("Force in Balance"),
                    //Keep both 18s DoTs rolling
                    Spell.Cast("Sever Force",
                        ret => !Core.Player.Target.HasMyDebuff("Sever Force") || Core.Player.Target.DebuffTimeLeft("Sever Force") <= 3),
                    Spell.Cast("Force Breach",
                        ret => !Core.Player.Target.HasMyDebuff("Force Breach") || Core.Player.Target.DebuffTimeLeft("Force Breach") <= 3),
                    //Crush Spirit finishes Spinning Strike's cooldown and lets it be used early
                    Spell.Cast("Spinning Strike",
                        ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Crush Spirit") || Core.Player.HasBuff("Stalker's Swiftness")),
                    Spell.Cast("Serenity Strike", ret => Core.Player.ForcePercent >= 30),
                    //Squelch off cooldown even without a Force Strike proc (low levels never proc it)
                    Spell.Cast("Squelch", ret => Core.Player.ForcePercent >= 50),
                    Spell.Cast("Double Strike", ret => Core.Player.ForcePercent >= 45),
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
                            Spell.CastOnGround("Force in Balance"),
                            Spell.Cast("Force Breach", ret => !Core.Player.Target.HasMyDebuff("Force Breach")),
                            Spell.Cast("Sever Force", ret => !Core.Player.Target.HasMyDebuff("Sever Force")),
                            //Cleaving Cut spreads both DoTs and heals for 50% of the damage dealt
							Spell.Cast("Cleaving Cut"))),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        //Whirling Blow is the AoE filler and also spreads the DoTs
                        Spell.Cast("Whirling Blow", ret => Core.Player.ForcePercent >= 40))
                    );
            }
        }
    }
}
