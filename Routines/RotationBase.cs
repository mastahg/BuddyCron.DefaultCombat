// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using System.Linq;
using BuddyCron.Inheritables;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Routines
{
    /// <summary>
    /// Base combat routine for one discipline, exposing its buff, cooldown, single-target, and
    /// area-of-effect priorities through the standard BuddyCron behavior hooks.
    /// </summary>
    public abstract class RotationBase : CombatRoutine
    {
        private Composite _combat;
        private Composite _outOfCombat;
        private Composite _pull;
        private System.DateTime _lastCombatIdleLogUtc = System.DateTime.MinValue;

        /// <inheritdoc />
        public abstract override string Name { get; }

        /// <summary>Gets the combat-style discipline implemented by this routine.</summary>
        public abstract CharacterDiscipline Discipline { get; }

        /// <inheritdoc />
        public sealed override CharacterDiscipline[] Class => new[] { Discipline };

        /// <inheritdoc />
        public sealed override float PullRange => 3f;

        /// <inheritdoc />
        public sealed override Composite PullBehavior => _pull;

        /// <summary>
        /// Builds this discipline's pull, combat, and out-of-combat behavior trees and registers
        /// the shared combat hotkeys.
        /// </summary>
        public sealed override void Initialize()
        {
            Logger.Write("*** Default Combat v90***");
            Logger.Write("Level: " + Core.Player.Level);
            Logger.Write("Class: " + Core.Player.CharacterClass);
            Logger.Write("Discipline: " + Core.Player.CharacterDiscipline);

            CombatHotkeys.Initialize();

            Logger.Write("Rotation Selected : " + Name);
            if (RotationRuntime.IsHealer)
            {
                Logger.Write("Healing Enabled");
            }

            _outOfCombat = new Decorator(
                ret => !Core.Player.IsDead && !Core.Player.IsMounted && !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    Targeting.ScanTargets,
                    new Decorator(ret => RotationRuntime.IsHealer, AreaOfEffect),
                    Spell.Buff(Core.Player.SelfBuffName()),
                    Buffs,
                    Rest.HandleRest,
                    Scavenge.ScavengeCorpse));

            _combat = new Decorator(
                ret => !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    Spell.WaitForCast(),
                    RotationRuntime.MedPack.UseItem(ret => Core.Player.HealthPercent <= 30),
                    Targeting.ScanTargets,
                    Cooldowns,
                    new Decorator(ret => RotationRuntime.IsHealer || CombatHotkeys.EnableAoe, AreaOfEffect),
                    SingleTarget,
                    new Action(ret => LogCombatIdleState())));

            _pull = new Decorator(
                ret => !CombatHotkeys.PauseRotation &&
                       (!RotationRuntime.MovementDisabled || RotationRuntime.IsHealer && !RotationRuntime.Grind),
                _combat);

            CombatBehavior = _combat;
            RestBehavior = _outOfCombat;
        }

        private RunStatus LogCombatIdleState()
        {
            if (Core.Player == null || !Core.Player.InCombat ||
                (System.DateTime.UtcNow - _lastCombatIdleLogUtc).TotalSeconds < 1.5)
            {
                return RunStatus.Failure;
            }

            _lastCombatIdleLogUtc = System.DateTime.UtcNow;
            try
            {
                var target = Core.Player.Target;
                Logger.Write(
                    "[CombatIdle] target={0} dead={1} combat={2} hostile={3} targetable={4} los={5} distance={6:F1} casting={7} cast={8} remaining={9:F2} moving={10} enemies={11} attackers={12}",
                    target != null ? target.Name : "none",
                    target != null && target.IsDead,
                    target != null && target.InCombat,
                    target != null && target.IsHostile,
                    target != null && target.IsTargetable,
                    target != null && target.InLineOfSight,
                    target != null ? target.Distance : -1,
                    Core.Player.IsCasting,
                    Core.Player.CastingAbility != null ? Core.Player.CastingAbility.Name : "none",
                    Core.Player.CastTimeRemaining,
                    Core.Player.IsMoving,
                    Targeting.Enemies != null ? Targeting.Enemies.Count : 0,
                    Core.Player.Attackers != null ? Core.Player.Attackers.Count() : 0);
            }
            catch (System.Exception exception)
            {
                Logger.Write("[CombatIdle] state read failed: " + exception.Message);
            }

            return RunStatus.Failure;
        }

        /// <summary>Gets the buff logic for this routine.</summary>
        public abstract Composite Buffs { get; }

        /// <summary>Gets the cooldown usage logic for this routine.</summary>
        public abstract Composite Cooldowns { get; }

        /// <summary>Gets the single-target logic for this routine.</summary>
        public abstract Composite SingleTarget { get; }

        /// <summary>Gets the area-of-effect logic for this routine.</summary>
        public abstract Composite AreaOfEffect { get; }
    }
}
