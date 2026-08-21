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
//using DefaultCombat.Extensions; ((Hold off for now))

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     7.x Sorcerer Corruption (healing) rotation. Healing lives in AreaOfEffect;
    ///     SingleTarget is Force-gated filler damage so a solo healer can still kill things.
    /// </summary>
    public class Corruption : RotationBase
    {
        private static readonly MultiDotProfile s_afflictionProfile = new MultiDotProfile
        {
            Key = "Corruption.Affliction",
            AbilityName = "Affliction",
            DebuffNames = new[] { "Affliction" },
            DebuffAbilitySpecIds = new[] { 0xE000892C91A4F3EAUL },
            MaxTargets = 1,
            ExpectedDurationSeconds = 18,
            RefreshWindowSeconds = 1.5,
            Enabled = () => AbilityManager.HasAbility("Affliction"),
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableAfflictionTarget,
            MinimumTtdSeconds = (target, selected) => 8
        };

        private static readonly MultiDotCoordinator s_afflictionCoordinator =
            new MultiDotCoordinator(s_afflictionProfile);

        public Corruption()
        {
            s_afflictionCoordinator.Reset();
        }

        private static bool IsUsableAfflictionTarget(HeroCharacter enemy) =>
            enemy != null && enemy.IsEngagedWithPlayer() && enemy.IsEffectivePvEHostile() &&
            enemy.IsTargetable && enemy.InLineOfSight && !enemy.IsDead;

        private static RunStatus TickAffliction(bool allowed)
        {
            return allowed ? s_afflictionCoordinator.Tick() : RunStatus.Failure;
        }

        public override CharacterDiscipline Discipline => CharacterDiscipline.Corruption;

        public override string Name => "Sorcerer Corruption";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Mark of Power")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(

                    //Break CC
                    Spell.Buff("Unbreakable Will", ret => Core.Player.IsStunned),

                    //Defensives
                    Spell.Buff("Force Barrier", ret => Core.Player.HealthPercent <= 15),
                    Spell.Buff("Unnatural Preservation", ret => Core.Player.HealthPercent <= 60),
                    Spell.HoT("Static Barrier", on => Core.Player, 100, ret => Core.Player.InCombat && !Core.Player.HasDebuff("Deionized")),

                    //Force management: dump Force Surge stacks with Consuming Darkness (no Weary when stacked)
                    Spell.Buff("Consuming Darkness", ret => NeedForce()),

                    //Healing cooldowns
                    Spell.Cast("Recklessness", ret => Targeting.ShouldAoeHeal),
                    Spell.Cast("Polarity Shift", ret => Targeting.ShouldAoeHeal),
                    Spell.Buff("Unlimited Power", ret => CombatHotkeys.EnableRaidBuffs),

                    //Companion
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    new Decorator(
                        ret => s_afflictionCoordinator.IsBusy,
                        new Action(ret => s_afflictionCoordinator.Continue())),
                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Filler damage so a solo/leveling healer can actually kill things.
                    //Only runs when nothing above (heals live in AreaOfEffect) wanted the GCD.
                    new Decorator(ret => Core.Player.Target != null &&
                        Core.Player.Target.IsEffectivePvEHostile() && !Core.Player.Target.IsDead,
                        new PrioritySelector(
                            Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                            new Action(ret => TickAffliction(Core.Player.ForcePercent >= 50)),
                            Spell.Cast("Volt Rush", ret => Core.Player.ForcePercent >= 50),   // lvl 68 choice, skipped if untrained
                            Spell.Cast("Shock", ret => Core.Player.ForcePercent >= 60),
                            Spell.Cast("Lightning Strike", ret => Core.Player.ForcePercent >= 70),
                            Spell.Cast("Saber Strike")
                            ))
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new PrioritySelector(

                    //Cleanse (Purge was renamed Expunge)
                    Spell.Cleanse("Expunge"),

                    //Use the instant, free Dark Concentration heal for urgent triage.
                    Spell.Heal("Dark Heal", 80, ret => Core.Player.HasBuff("Dark Concentration")),

                    //Prevent predictable damage, then build Force Bending with Resurgence.
                    Spell.HoT("Static Barrier", 90, ret => !Targeting.HealTarget.HasDebuff("Deionized")),
                    Spell.HoT("Resurgence", 95),

                    //Spend Force Bending on the efficient channel before other consumers.
                    new Decorator(ret => Core.Player.HasBuff("Force Bending"),
                        new PrioritySelector(
                            Spell.Heal("Innervate", 90),
                            Spell.Heal("Roaming Mend", 90,
                                ret => !Core.Player.HasBuff("Roaming Mend Charges")),
                            Spell.Heal("Dark Infusion", 60)
                            )),

                    //Innervate builds Force Surge; Mend is strongest when several allies are hurt.
                    Spell.Heal("Innervate", 85),
                    Spell.Heal("Roaming Mend", 85,
                        ret => !Core.Player.HasBuff("Roaming Mend Charges") &&
                               (Targeting.ShouldAoeHeal || Targeting.HealTarget.HealthPercent <= 60)),

                    //Use the ground heal only for a sustained, tightly grouped raid-healing check.
                    Spell.HealGround("Revivification", ret => Targeting.AoeHealCount >= 4),

                    //Maintain preventative effects on the active tank after immediate triage.
                    Spell.HoT("Static Barrier", on => Targeting.Tank, 100,
                        ret => Targeting.Tank != null && Targeting.Tank.InCombat && !Targeting.Tank.HasDebuff("Deionized")),
                    Spell.HoT("Resurgence", on => Targeting.Tank, 100, ret => Targeting.Tank != null && Targeting.Tank.InCombat),

                    //Dark Infusion is the efficient direct filler. At the earliest levels, Dark Heal
                    //must fill that role because the character has not learned Dark Infusion yet.
                    Spell.Heal("Dark Heal", 80, ret => Core.Player.Level < 15),
                    Spell.Heal("Dark Heal", 35),
                    Spell.Heal("Dark Infusion", 80));
            }
        }

        /// <summary>
        ///     True when Consuming Darkness should be used: with Force Surge stacks below 80%
        ///     Force (no Weary penalty), or starved at 20% Force or less without Weary already up.
        /// </summary>
        private bool NeedForce()
        {
            //Force Surge stacks (from Innervate crits) make Consuming Darkness free of the Weary penalty.
            if (Core.Player.HasBuff("Force Surge") && Core.Player.ForcePercent < 80)
                return true;

            //Starved: take the Weary hit rather than stall out with no Force.
            if (Core.Player.ForcePercent <= 20 && !Core.Player.HasDebuff("Weary"))
                return true;

            return false;
        }
    }
}
