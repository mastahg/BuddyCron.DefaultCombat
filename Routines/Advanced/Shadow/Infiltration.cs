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
    ///     7.x Shadow Infiltration (stealth melee burst DPS) rotation: opens from stealth with
    ///     Shadow Stride and spends 3-stack Breaching Shadows on Force Breach.
    /// </summary>
    public class Infiltration : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Infiltration;

        public override string Name => "Shadow Infiltration";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Force Valor"),
                    //Always re-stealth out of combat so we can open with Shadow Stride + Vaulting Slash
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
                    Spell.Buff("Battle Readiness", ret => Core.Player.InCombat),
                    Spell.Buff("Deflection", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Resilience", ret => Core.Player.HealthPercent <= 50),
                    //Force Potency is what turns the 3 stack Force Breach into an autocrit
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
                    //Stealth opener / gap closer
                    Spell.Cast("Shadow Stride", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),
                    Spell.Cast("Force Speed", ret => CombatHotkeys.EnableCharge && Core.Player.IsMoving && Core.Player.Target.EdgeDistance > 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupts
                    Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Force Stun", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Refresh Clairvoyance before spending charges so Psychokinetic Blast retains its bonuses.
                    Spell.Cast("Clairvoyant Strike",
                        ret => Core.Player.ForcePercent >= 25 &&
                               (!Core.Player.HasBuff("Clairvoyance") || Core.Player.BuffTimeLeft("Clairvoyance") <= 3)),

                    //Spend three Breaching Shadows when that mechanic is active. The zero-stack CanCast
                    //fallback lets the pre-upgrade low-level version fire without guessing an unlock level.
                    Spell.Cast("Force Breach",
                        ret => Core.Player.BuffCount("Breaching Shadows") >= 3 ||
                               (Core.Player.BuffCount("Breaching Shadows") == 0 &&
                                AbilityManager.CanCast("Force Breach", Core.Player.Target).Success)),
                    Spell.Cast("Spinning Strike", ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Stalker's Swiftness")),
                    //Vaulting Slash is the hardest hitting ability - use it on cooldown
                    Spell.Cast("Vaulting Slash"),
                    //Psychokinetic Blast goes on cooldown regardless of Circling Shadows stacks
                    Spell.Cast("Psychokinetic Blast"),
                    Spell.Cast("Shadow Strike", ret => Core.Player.HasBuff("Infiltration Tactics")),
                    //Clairvoyant Strike is the filler and keeps Clairvoyance up
                    Spell.Cast("Clairvoyant Strike", ret => Core.Player.ForcePercent >= 25),
                    //Filler for characters that have not trained Clairvoyant Strike yet
                    Spell.Cast("Double Strike", ret => Core.Player.ForcePercent >= 25),
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
                            Spell.Cast("Force Breach",
                                ret => Core.Player.BuffCount("Breaching Shadows") >= 3 ||
                                       (Core.Player.BuffCount("Breaching Shadows") == 0 &&
                                        AbilityManager.CanCast("Force Breach", Core.Player.Target).Success)),
							Spell.Cast("Cleaving Cut"))),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        //Whirling Blow is the AoE filler and still procs Shadow Technique
                        Spell.Cast("Whirling Blow", ret => Core.Player.ForcePercent >= 40))
                    );
            }
        }
    }
}
