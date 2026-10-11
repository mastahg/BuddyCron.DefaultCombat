// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System.Windows.Input;
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
    /// <summary>
    ///     Guardian Defense (tank) rotation: off-GCD Riposte, Warding Strike buff upkeep,
    ///     Guardian Slash and Blade Storm on cooldown.
    /// </summary>
    public class Defense : RotationBase
    {
        // Force Sweep is a self-cast 5.1 m sphere around us (abl.jedi_knight.sweep).
        private const float ForceSweepRadius = 0.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Defense;

        public override string Name => "Guardian Defense";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Force Might")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Resolute", ret => Core.Player.IsStunned),

                    //Focus generation. Threatening Focus is a passive that upgrades Combat Focus
                    //(taunt cooldown reduction) — not a castable ability. Combat Focus is the button.
                    Spell.Cast("Combat Focus", ret => Core.Player.ActionPoints <= 6),

                    //Defensives
                    Spell.Buff("Saber Reflect", ret => Core.Player.HealthPercent <= 90),
                    Spell.Buff("Enure", ret => Core.Player.HealthPercent <= 80),
                    Spell.Buff("Focused Defense", ret => Core.Player.HealthPercent < 70),
                    Spell.Buff("Warding Call", ret => Core.Player.HealthPercent <= 50),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 30),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Force Leap", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),
                    Spell.Cast("Saber Throw", ret => Core.Player.Target.EdgeDistance > .4f && Core.Player.Target.EdgeDistance <= 3f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupts
                    Spell.Cast("Force Kick", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Rotation - Riposte is off the GCD, so fire it whenever it is up (Blade Barricade)
                    Spell.Cast("Riposte"),

                    //Warding Strike is the focus builder and buffs the next Guardian Slash into an AoE
                    Spell.Cast("Warding Strike", ret => !Core.Player.HasBuff("Warding Strike") || Core.Player.ActionPoints <= 6),
                    Spell.Cast("Guardian Slash"),
                    Spell.Cast("Blade Storm"),
                    Spell.Cast("Dispatch", ret => Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Blade Barrage"),
                    Spell.Cast("Hilt Bash", ret => !Core.Player.Target.IsStunned && !Core.Player.Target.BossOrGreater() && Core.Player.Target.EdgeDistance <= 0.4f),
                    //Force Sweep is a 5 m sphere around us, not the target.
                    Spell.Cast("Force Sweep", ret => Core.Player.Target.Distance <= ForceSweepRadius),

                    //Fillers
                    Spell.Cast("Saber Throw", ret => Core.Player.ActionPoints >= 3),
                    Spell.Cast("Slash", ret => Core.Player.ActionPoints >= 6),
                    Spell.Cast("Strike")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldPbaoe,
                    new PrioritySelector(
                        //Guardian Slash only cleaves while Warding Strike's buff is up
                        Spell.Cast("Warding Strike", ret => !Core.Player.HasBuff("Warding Strike")),
                        //With Warding Strike up, Guardian Slash cleaves a 5 m sphere around the target.
                        Spell.Cast("Guardian Slash", ret => (Core.Player.HasBuff("Warding Strike") || Core.Player.Level < 30) && Targeting.ShouldTargetAoe(0.5f)),
                        Spell.Cast("Blade Storm"),
                        //Force Sweep is a 5 m sphere around us, not the target.
                        Spell.Cast("Force Sweep", ret => Targeting.ShouldPointBlankAoe(ForceSweepRadius)),
                        //Cyclone Slash is a 5 m, 179-degree forward cone.
                        Spell.Cast("Cyclone Slash", ret => Targeting.ShouldFrontalConeAoe(0.5f, 179f)),
                        Spell.Cast("Blade Barrage")
                        ));
            }
        }
    }
}
