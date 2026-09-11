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
    ///     7.x Sentinel Watchman (melee DoT DPS) rotation: keeps the Cauterize / Overload Saber /
    ///     Force Melt burns rolling and lines Zen autocrits up with them.
    /// </summary>
    public class Watchman : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Watchman;

        public override string Name => "Sentinel Watchman";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Force Might")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Resolute", ret => Core.Player.IsStunned),

                    //Defensives -- keep these first, they are what keeps a leveling character alive
                    Spell.Buff("Rebuke", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 50),
                    Spell.Cast("Force Camouflage", ret => Core.Player.HealthPercent <= 35),
                    Spell.Buff("Guarded by the Force", ret => Core.Player.HealthPercent <= 20),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Offensive cooldowns
                    Spell.Buff("Force Clarity", ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater()),
                    Spell.Buff("Inspiration", ret => CombatHotkeys.EnableRaidBuffs),

                    //Overload Saber is off-GCD. Force Melt below waits for two melee-applied stacks so
                    //its non-melee GCD does not delay the three-stack application sequence.
                    Spell.Cast("Overload Saber", ret => !Core.Player.HasBuff("Overload Saber")),

                    //Valorous Call tops Centering back up so Zen comes around again sooner
                    Spell.Cast("Valorous Call", ret => !Core.Player.HasBuff("Zen") && Core.Player.BuffCount("Centering") < 10),

                    //Zen makes the burns autocrit -- only worth it once the burns are actually rolling
                    Spell.Cast("Zen",
                        ret => !Core.Player.HasBuff("Zen") && Core.Player.Target != null &&
                               (Core.Player.Target.HasDebuff("Burning (Cauterize)") ||
                                Core.Player.Target.HasDebuff("Burning (Overload Saber)") ||
                                Core.Player.Level < 30))
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Force Leap", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Rotation
                    Spell.Cast("Force Kick", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Spend Zen's autocrit window on Force Melt after two Overload Saber stacks, then
                    //let the next melee hit apply the third stack.
                    Spell.Cast("Force Melt",
                        ret => !AbilityManager.HasAbility("Overload Saber") || !Core.Player.HasBuff("Overload Saber") ||
                               Core.Player.Target.DebuffCount("Burning (Overload Saber)") >= 2),
                    Spell.Cast("Merciless Slash"),

                    //Maintain Cauterize after the two defining discipline attacks.
                    Spell.Cast("Cauterize",
                        ret => !Core.Player.Target.HasMyDebuff("Burning (Cauterize)") ||
                               Core.Player.Target.DebuffTimeLeft("Burning (Cauterize)") <= 2),
                    Spell.Cast("Dispatch", ret => Core.Player.Target.HealthPercent <= 30),

                    //Mind Sear makes the next Twin Saber Throw hit twice as hard and resets its cooldown
                    Spell.Cast("Twin Saber Throw", ret => Core.Player.HasBuff("Mind Sear") && Core.Player.Target.EdgeDistance <= 1f),

                    //Fillers -- Blade Barrage is free, so it comes before the focus spenders
                    Spell.Cast("Blade Barrage"),
                    Spell.Cast("Slash", ret => Core.Player.ActionPoints >= 6),
                    Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8),
                    Spell.Cast("Twin Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),

                    //Never stall -- free basic attack that builds focus
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
                        //Burns first -- Force Sweep spreads them to everything around the target
                        Spell.Cast("Cauterize", ret => !Core.Player.Target.HasMyDebuff("Burning (Cauterize)")),
                        Spell.Cast("Force Melt",
                            ret => !AbilityManager.HasAbility("Overload Saber") || !Core.Player.HasBuff("Overload Saber") ||
                                   Core.Player.Target.DebuffCount("Burning (Overload Saber)") >= 2),
                        Spell.Cast("Twin Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),
                        Spell.Cast("Force Sweep"),
                        Spell.Cast("Merciless Slash"),
                        Spell.Cast("Cyclone Slash", ret => Core.Player.ActionPoints >= 5),
                        Spell.Cast("Blade Barrage"),
                        Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8)
                        ));
            }
        }
    }
}
