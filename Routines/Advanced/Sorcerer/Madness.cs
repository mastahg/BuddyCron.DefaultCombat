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
    ///     7.x Sorcerer Madness (ranged DoT DPS) rotation: keeps Affliction and Creeping Terror
    ///     up, spreads them with Death Field and spends 4-stack Wrath on Demolish.
    /// </summary>
    public class Madness : RotationBase
    {
        private const int MaximumMaintainedDotTargets = 2;
        private const double SelectedDotMinimumTtd = 5;
        private const double SecondaryDotMinimumTtd = 8;
        private const double DemolishMinimumTtd = 8;

        private static readonly MultiDotProfile s_afflictionProfile = new MultiDotProfile
        {
            Key = "Madness.Affliction",
            AbilityName = "Affliction",
            DebuffNames = new[] { "Affliction" },
            DebuffAbilitySpecIds = new[] { 0xE000892C91A4F3EAUL },
            MaxTargets = MaximumMaintainedDotTargets,
            ExpectedDurationSeconds = 18,
            RefreshWindowSeconds = 1.5,
            Enabled = () => HasAffliction,
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableRangedDotTarget,
            MinimumTtdSeconds = (target, selected) =>
                selected ? SelectedDotMinimumTtd : SecondaryDotMinimumTtd
        };

        private static readonly MultiDotProfile s_creepingTerrorProfile = new MultiDotProfile
        {
            Key = "Madness.CreepingTerror",
            AbilityName = "Creeping Terror",
            DebuffNames = new[] { "Creeping Terror" },
            MaxTargets = MaximumMaintainedDotTargets,
            ExpectedDurationSeconds = 18,
            RefreshWindowSeconds = 1.5,
            Enabled = () => HasCreepingTerror,
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableRangedDotTarget,
            MinimumTtdSeconds = (target, selected) =>
                selected ? SelectedDotMinimumTtd : SecondaryDotMinimumTtd
        };

        private static readonly MultiDotProfile s_demolishProfile = new MultiDotProfile
        {
            Key = "Madness.Demolish",
            AbilityName = "Demolish",
            DebuffNames = new[] { "Demolish", "Demolished" },
            MaxTargets = 1,
            ExpectedDurationSeconds = 6,
            RefreshWindowSeconds = 0.5,
            Enabled = () => HasDemolish,
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableRangedDotTarget,
            MinimumTtdSeconds = (target, selected) => DemolishMinimumTtd
        };

        private static readonly MultiDotCoordinator s_maintainedDotCoordinator =
            new MultiDotCoordinator(s_afflictionProfile, s_creepingTerrorProfile);

        private static readonly MultiDotCoordinator s_demolishCoordinator =
            new MultiDotCoordinator(s_demolishProfile);

        public Madness()
        {
            s_maintainedDotCoordinator.Reset();
            s_demolishCoordinator.Reset();
        }

        private static bool HasAffliction => AbilityManager.HasAbility("Affliction");

        private static bool HasCreepingTerror => AbilityManager.HasAbility("Creeping Terror");

        private static bool HasDemolish => AbilityManager.HasAbility("Demolish");

        private static bool IsUsableRangedDotTarget(HeroCharacter enemy) =>
            enemy != null && enemy.IsEngagedWithPlayer() && enemy.IsEffectivePvEHostile() &&
            enemy.IsTargetable && enemy.InLineOfSight && !enemy.IsDead;

        private static bool CurrentTargetHasAffliction =>
            Core.Player.Target != null &&
            s_maintainedDotCoordinator.IsMaintained(s_afflictionProfile, Core.Player.Target);

        private static RunStatus TickDemolish(bool allowed)
        {
            return allowed ? s_demolishCoordinator.Tick() : RunStatus.Failure;
        }

        public override CharacterDiscipline Discipline => CharacterDiscipline.Madness;

        public override string Name => "Sorcerer Madness";


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

                    //Force management (Consuming Darkness applies Weary without Force Surge, so only use it starved)
                    Spell.Buff("Consuming Darkness", ret => Core.Player.ForcePercent <= 25 && !Core.Player.HasDebuff("Weary")),

                    //Align throughput cooldowns with an established DoT window on durable targets.
                    Spell.Cast("Polarity Shift",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (CurrentTargetHasAffliction || !HasAffliction)),
                    Spell.Cast("Recklessness",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (CurrentTargetHasAffliction || !HasAffliction)),
                    Spell.Buff("Force Speed", ret => Core.Player.IsMoving),
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
                        ret => s_maintainedDotCoordinator.IsBusy,
                        new Action(ret => s_maintainedDotCoordinator.Continue())),
                    new Decorator(
                        ret => s_demolishCoordinator.IsBusy,
                        new Action(ret => s_demolishCoordinator.Continue())),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt (Electrocute is the backup interrupt, bosses are stun immune)
                    Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Electrocute",
                        ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts && !Core.Player.Target.BossOrGreater()),

                    //Consume Wrath carried from a previous channel before refreshing setup effects.
                    new Action(ret => TickDemolish(Core.Player.BuffCount("Wrath") >= 4)),

                    //DoTs first, everything else in Madness scales off them.
                    new Action(ret => s_maintainedDotCoordinator.Tick()),

                    //Rotation
                    Spell.CastOnGround("Death Field"),   // applies Deathmark, spreads DoTs
                    new Action(ret => TickDemolish(
                        Core.Player.BuffCount("Wrath") >= 4 || Core.Player.Level < 50)),
                    Spell.Cast("Force Leech"),
                    Spell.Cast("Lightning Strike", ret => Core.Player.BuffCount("Wrath") >= 4),
                    Spell.Cast("Shock", ret => Core.Player.Level < 27),   // pre-Plague Master filler only

                    //Filler / Wrath builder
                    Spell.Cast("Force Lightning"),
                    Spell.Cast("Saber Strike", ret => Core.Player.ForcePercent <= 25)
                    );
            }
        }


        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        new Decorator(
                            ret => s_maintainedDotCoordinator.IsBusy,
                            new Action(ret => s_maintainedDotCoordinator.Continue())),
                        new Decorator(
                            ret => s_demolishCoordinator.IsBusy,
                            new Action(ret => s_demolishCoordinator.Continue())),
                        new Action(ret => s_maintainedDotCoordinator.Tick()),
                        Spell.CastOnGround("Death Field"),
                        new Action(ret => TickDemolish(
                            Core.Player.BuffCount("Wrath") >= 4 || Core.Player.Level < 50)),
                        Spell.CastOnGround("Force Storm"),
                        Spell.Cast("Force Lightning")
                        ));
            }
        }
    }
}
