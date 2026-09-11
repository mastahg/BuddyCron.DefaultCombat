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
    ///     7.x Shadow Kinetic Combat (tank) rotation: keeps Kinetic Ward up, builds Harnessed
    ///     Shadows with Project / Slow Time and spends them on Cascading Debris.
    /// </summary>
    public class KineticCombat : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.KineticCombat;

        public override string Name => "Shadow Kinetic Combat";

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Force Valor"),
                    //Kinetic Combat should always open from Stealth: Shadow Stride out of stealth
                    //grants Shadow Wrap (free, full damage Shadow Strike) and Shadow Protection stacks.
                    Spell.Buff("Stealth", ret => !Rest.KeepResting() && !RotationRuntime.MovementDisabled && !Core.Player.IsMounted)
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    //Kinetic Ward: keep up 100% of the time, refresh when it drops or runs low on charges
                    Spell.Cast("Kinetic Ward", ret => Core.Player,
                        ret => !Core.Player.HasBuff("Kinetic Ward") || Core.Player.BuffCount("Kinetic Ward") <= 3),
                    Spell.Buff("Force of Will", ret => Core.Player.IsStunned),
                    Spell.Buff("Battle Readiness", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Deflection", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Resilience", ret => Core.Player.HealthPercent <= 50),
                    Spell.Buff("Force Speed", ret => Core.Player.HealthPercent <= 35),
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
                    //Gap closer. Shadow Stride also grants Shadow Wrap, so the opener naturally
                    //lands a free, full damage Shadow Strike right after the stride.
                    Spell.Cast("Shadow Stride", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Interrupts
                    Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Force Stun", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Rotation
                    Spell.Cast("Spinning Strike", ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Stalker's Swiftness")),
                    //Cascading Debris is only worth spending at 3 Harnessed Shadows (Project and Slow Time
                    //each build a stack). Low levels have no stacks to build, so let them use it as a nuke.
                    Spell.Cast("Cascading Debris", ret => Core.Player.BuffCount("Harnessed Shadows") >= 3 || Core.Player.Level < 30),
                    //Project and Slow Time on cooldown - they are the Harnessed Shadows builders.
                    //Particle Acceleration resets Project for free, CanCast picks that up automatically.
                    Spell.Cast("Project"),
                    Spell.Cast("Slow Time"),
                    Spell.Cast("Shadow Strike", ret => Core.Player.HasBuff("Shadow Wrap")),
                    //Combat Technique's Force Breach applies the accuracy debuff "Unsteady (Force)" (45s)
                    Spell.Cast("Force Breach", ret => !Core.Player.Target.HasMyDebuff("Unsteady (Force)")),
                    Spell.Cast("Double Strike", ret => Core.Player.ForcePercent >= 30),
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
                            //Cascading Debris is worth using in AoE even at 0 Harnessed Shadows
                            Spell.Cast("Cascading Debris"),
                            Spell.Cast("Slow Time"),
                            Spell.Cast("Force Breach"),
                            Spell.Cast("Cleaving Cut"))),
                    new Decorator(ret => Targeting.ShouldPbaoe,
                        Spell.Cast("Whirling Blow", ret => Core.Player.ForcePercent >= 40))
                    );
            }
        }
    }
}
