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
    ///     Marauder Annihilation (DoT melee dps) rotation: keeps the Force Rend and Rupture
    ///     bleeds rolling and autocrits them with Berserk.
    /// </summary>
    public class Annihilation : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Annihilation;

        public override string Name => "Marauder Annihilation";

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

                    //Deadly Saber is off-GCD. Force Rend below waits for two melee-applied stacks so
                    //its non-melee GCD does not delay the three-stack application sequence.
                    Spell.Cast("Deadly Saber", ret => !Core.Player.HasBuff("Deadly Saber")),

                    //Frenzy tops Fury back up so Berserk comes around again sooner
                    Spell.Cast("Frenzy", ret => !Core.Player.HasBuff("Berserk") && Core.Player.BuffCount("Fury") < 10),

                    //Berserk makes bleeds autocrit -- only worth it once bleeds are actually rolling
                    Spell.Cast("Berserk",
                        ret => !Core.Player.HasBuff("Berserk") && Core.Player.Target != null &&
                               (Core.Player.Target.HasDebuff("Bleeding (Deadly Saber)") ||
                                Core.Player.Target.HasDebuff("Bleeding (Rupture)") ||
                                Core.Player.Level < 30))
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

                    //Spend Berserk's autocrit window on Force Rend after two Deadly Saber stacks, then
                    //let the next melee hit apply the third stack.
                    Spell.Cast("Force Rend",
                        ret => (!AbilityManager.HasAbility("Deadly Saber") || !Core.Player.HasBuff("Deadly Saber") ||
                                Core.Player.Target.DebuffCount("Bleeding (Deadly Saber)") >= 2) &&
                               (!Core.Player.Target.HasMyDebuff("Force Rend") ||
                                Core.Player.Target.DebuffTimeLeft("Force Rend") <= 2)),
                    Spell.Cast("Annihilate"),

                    //Maintain Rupture after the two defining discipline attacks.
                    Spell.Cast("Rupture",
                        ret => !Core.Player.Target.HasMyDebuff("Bleeding (Rupture)") ||
                               Core.Player.Target.DebuffTimeLeft("Bleeding (Rupture)") <= 2),

                    //Pulverize makes Dual Saber Throw free + resets its cooldown.
                    Spell.Cast("Dual Saber Throw", ret => Core.Player.HasBuff("Pulverize") && Core.Player.Target.EdgeDistance <= 1f),
                    Spell.Cast("Vicious Throw", ret => Core.Player.Target.HealthPercent <= 30),
                    Spell.Cast("Ravage", ret => Core.Player.HasBuff("Berserk")),

                    //Fillers
                    Spell.Cast("Vicious Slash", ret => Core.Player.ActionPoints >= 6),
                    Spell.Cast("Ravage"),
                    Spell.Cast("Battering Assault", ret => Core.Player.ActionPoints <= 8),

                    //Low level: no Pulverize passive yet, so Dual Saber Throw is just a rage builder
                    Spell.Cast("Dual Saber Throw", ret => Core.Player.Level < 40 && Core.Player.Target.EdgeDistance <= 1f),
                    Spell.Cast("Force Scream",
                        ret => Core.Player.Target.EdgeDistance > Distance.Melee && Core.Player.Target.EdgeDistance <= 1f),
                    Spell.Cast("Force Charge", ret => CombatHotkeys.EnableCharge && Core.Player.ActionPoints <= 8),

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
                        //Bleeds first -- Smash/Rupture spread them (Blood Wave) and Smash bounces Force Rend
                        Spell.Cast("Force Rend",
                            ret => (!AbilityManager.HasAbility("Deadly Saber") || !Core.Player.HasBuff("Deadly Saber") ||
                                    Core.Player.Target.DebuffCount("Bleeding (Deadly Saber)") >= 2) &&
                                   !Core.Player.Target.HasMyDebuff("Force Rend")),
                        Spell.Cast("Rupture", ret => !Core.Player.Target.HasMyDebuff("Bleeding (Rupture)")),
                        Spell.Cast("Dual Saber Throw", ret => Core.Player.Target.EdgeDistance <= 1f),
                        Spell.Cast("Smash"),
                        Spell.Cast("Annihilate"),
                        Spell.Cast("Sweeping Slash", ret => Core.Player.ActionPoints >= 5),
                        Spell.Cast("Ravage"),
                        Spell.Cast("Battering Assault", ret => Core.Player.ActionPoints <= 8)
                        ));
            }
        }
    }
}
