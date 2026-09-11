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
    ///     7.x Sentinel Concentration (melee burst DPS) rotation: sets up Felling Blow and
    ///     Singularity, then cashes them in with Focused Burst / Force Sweep.
    /// </summary>
    public class Concentration : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Concentration;

        public override string Name => "Sentinel Concentration";

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

                    //Valorous Call tops Centering back up so Zen comes around again sooner
                    Spell.Cast("Valorous Call", ret => !Core.Player.HasBuff("Zen") && Core.Player.BuffCount("Centering") < 10),

                    //Zen grants Singularity -- feeds the next Focused Burst / Force Sweep, so do not
                    //overwrite a Singularity that has not been cashed in yet
                    Spell.Cast("Zen", ret => !Core.Player.HasBuff("Zen") && !Core.Player.HasBuff("Singularity"))
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

                    //Set the burst window up: Zealous Leap grants Felling Blow (autocrit) + Momentum,
                    //Force Exhaustion / Zen grant Singularity (free + 15% damage)
                    Spell.Cast("Zealous Leap",
                        ret => CombatHotkeys.EnableCharge && !Core.Player.HasBuff("Felling Blow")),
                    Spell.Cast("Force Exhaustion", ret => !Core.Player.HasBuff("Singularity")),

                    //Cash the procs in
                    Spell.Cast("Focused Burst", ret => Core.Player.HasBuff("Singularity") || Core.Player.HasBuff("Felling Blow")),

                    //Concentrated Slice is the main focus spender (it also applies Beat Down)
                    Spell.Cast("Concentrated Slice", ret => Core.Player.ActionPoints >= 5),
                    Spell.Cast("Dispatch", ret => Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Blade Storm",
                        ret => (Core.Player.HasBuff("Momentum") || !AbilityManager.HasAbility("Zealous Leap")) &&
                               Core.Player.Target.EdgeDistance <= 1f),

                    //Focused Burst on cooldown even without a proc
                    Spell.Cast("Focused Burst"),

                    //Fillers -- Blade Barrage is free and still builds Centering
                    Spell.Cast("Blade Barrage"),
                    Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8),
                    Spell.Cast("Slash", ret => Core.Player.ActionPoints >= 6),
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
                        //Force Sweep is the AoE stand-in for Focused Burst and eats the same procs
                        Spell.Cast("Force Exhaustion", ret => !Core.Player.HasBuff("Singularity")),
                        Spell.Cast("Force Sweep",
                            ret => Core.Player.HasBuff("Singularity") || Core.Player.HasBuff("Felling Blow") ||
                                   !AbilityManager.HasAbility("Focused Burst")),
                        Spell.Cast("Zealous Leap", ret => CombatHotkeys.EnableCharge),
                        Spell.Cast("Force Sweep"),
                        Spell.Cast("Twin Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),
                        Spell.Cast("Cyclone Slash", ret => Core.Player.ActionPoints >= 5),
                        Spell.Cast("Blade Barrage"),
                        Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8)
                        ));
            }
        }
    }
}
