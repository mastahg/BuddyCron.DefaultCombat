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
    ///     Marauder Carnage (burst melee dps) rotation: opens the Ferocity armour-pen window
    ///     and spends it on Devastating Blast / Gore / Vicious Throw.
    /// </summary>
    public class Carnage : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Carnage;

        public override string Name => "Marauder Carnage";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Unnatural Might")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Unleash", ret => Core.Player.IsStunned),

                    //Defensives -- keep these first, they are what keeps a leveling character alive
                    Spell.Buff("Cloak of Pain", ret => Core.Player.HealthPercent <= 75),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 50),
                    Spell.Cast("Force Camouflage", ret => Core.Player.HealthPercent <= 35),
                    Spell.Buff("Undying Rage", ret => Core.Player.HealthPercent <= 20),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15),

                    //Offensive cooldowns
                    Spell.Buff("Furious Power", ret => Core.Player.Target.StrongOrGreater()),
                    Spell.Buff("Bloodthirst", ret => CombatHotkeys.EnableRaidBuffs),

                    //Frenzy tops Fury back up so Berserk comes around again sooner
                    Spell.Cast("Frenzy", ret => !Core.Player.HasBuff("Berserk") && Core.Player.BuffCount("Fury") < 10),
                    Spell.Cast("Berserk", ret => !Core.Player.HasBuff("Berserk"))
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Force Charge", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Rotation
                    Spell.Cast("Disruption", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Enter the burst window with enough rage and a live Massacre Ataru buff.
                    Spell.Cast("Battering Assault", ret => Core.Player.ActionPoints <= 5),
                    Spell.Cast("Massacre",
                        ret => Core.Player.ActionPoints >= 3 &&
                               (!Core.Player.HasBuff("Massacre") || Core.Player.BuffTimeLeft("Massacre") <= 2)),
                    Spell.Cast("Ferocity",
                        ret => Core.Player.Target.EdgeDistance <= Distance.Melee &&
                               (Core.Player.HasBuff("Massacre") || Core.Player.Level < 30)),

                    //Spend the window on the hardest hitters, in guide order
                    Spell.Cast("Devastating Blast", ret => Core.Player.HasBuff("Execute") || Core.Player.Level < 30),
                    Spell.Cast("Vicious Throw",
                        ret => Core.Player.HasBuff("Slaughter") || Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Gore"),
                    Spell.Cast("Devastating Blast"),

                    //Dual Saber Throw on cooldown -- damage plus rage
                    Spell.Cast("Dual Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),

                    //Ravage is free, so it is the better filler while Berserk is up or when rage-starved
                    Spell.Cast("Ravage", ret => Core.Player.HasBuff("Berserk") || Core.Player.ActionPoints < 3),

                    //Primary filler
                    Spell.Cast("Massacre", ret => Core.Player.ActionPoints >= 3),
                    Spell.Cast("Battering Assault", ret => Core.Player.ActionPoints <= 8),
                    Spell.Cast("Ravage"),

                    //Never stall -- free basic attack
                    Spell.Cast("Assault")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldPbaoe,
                    new PrioritySelector(
                        //These out-damage Sweeping Slash at any target count, so they stay on top
                        Spell.Cast("Dual Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),
                        Spell.Cast("Ferocity", ret => Core.Player.Target.EdgeDistance <= Distance.MeleeAoE),
                        Spell.Cast("Battering Assault", ret => Core.Player.ActionPoints <= 8),

                        //Sweeping Slash beats the single-target fillers from 2 targets up
                        Spell.Cast("Sweeping Slash", ret => Core.Player.ActionPoints >= 5),

                        Spell.Cast("Devastating Blast", ret => Core.Player.HasBuff("Execute") || Core.Player.Level < 30),
                        Spell.Cast("Vicious Throw",
                            ret => Core.Player.HasBuff("Slaughter") || Core.Player.Target.HealthPercent <= 30),
                        Spell.Cast("Ravage")
                        ));
            }
        }
    }
}
