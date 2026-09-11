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
    ///     7.x Sentinel Combat (melee burst DPS) rotation: opens armour-pen windows with
    ///     Precision and spends them on Clashing Blast / Dispatch / Blade Storm.
    /// </summary>
    public class Combat : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Combat;

        public override string Name => "Sentinel Combat";

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

                    //Zen gives alacrity plus a third Precision charge -- just use it on cooldown
                    Spell.Cast("Zen", ret => !Core.Player.HasBuff("Zen"))
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

                    //Enter the burst window with enough focus and a live Blade Rush Ataru buff.
                    Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 5),
                    Spell.Cast("Blade Rush",
                        ret => Core.Player.ActionPoints >= 3 &&
                               (!Core.Player.HasBuff("Blade Rush") || Core.Player.BuffTimeLeft("Blade Rush") <= 2)),
                    Spell.Cast("Precision",
                        ret => Core.Player.Target.EdgeDistance <= Distance.Melee &&
                               (Core.Player.HasBuff("Blade Rush") || Core.Player.Level < 30)),

                    //Spend the window on the hardest hitters. Opportune Attack autocrits Clashing Blast,
                    //Hand of Justice makes Dispatch free and usable at any health.
                    Spell.Cast("Clashing Blast", ret => Core.Player.HasBuff("Opportune Attack") || Core.Player.Level < 30),
                    Spell.Cast("Dispatch",
                        ret => Core.Player.HasBuff("Hand of Justice") || Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Lance"),
                    Spell.Cast("Clashing Blast"),
                    Spell.Cast("Blade Storm", ret => Core.Player.HasBuff("Precision") && Core.Player.ActionPoints >= 6),

                    //Twin Saber Throw on cooldown -- free damage
                    Spell.Cast("Twin Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),

                    //Blade Barrage is free, so it is the better filler while Zen is up or focus-starved
                    Spell.Cast("Blade Barrage", ret => Core.Player.HasBuff("Zen") || Core.Player.ActionPoints < 3),

                    //Primary filler
                    Spell.Cast("Blade Rush", ret => Core.Player.ActionPoints >= 3),
                    Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8),
                    Spell.Cast("Blade Barrage"),

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
                        //These out-damage Cyclone Slash at any target count, so they stay on top
                        Spell.Cast("Twin Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),
                        Spell.Cast("Precision", ret => Core.Player.Target.EdgeDistance <= Distance.MeleeAoE),
                        Spell.Cast("Force Sweep"),
                        Spell.Cast("Zealous Strike", ret => Core.Player.ActionPoints <= 8),

                        //Cyclone Slash beats the single-target fillers from 2 targets up
                        Spell.Cast("Cyclone Slash", ret => Core.Player.ActionPoints >= 5),

                        Spell.Cast("Clashing Blast", ret => Core.Player.HasBuff("Opportune Attack") || Core.Player.Level < 30),
                        Spell.Cast("Dispatch",
                            ret => Core.Player.HasBuff("Hand of Justice") || Core.Player.Target.HealthPercent <= 30),
                        Spell.Cast("Blade Barrage")
                        ));
            }
        }
    }
}
