using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Routines
{
    internal sealed class LightningPvP : AutomaticRoutineEngine
    {
        private enum CombatMode
        {
            Pressure,
            Burst,
            Focused,
            Freecasting,
            Execute,
            Recovery
        }

        private readonly List<HeroPlayer> _enemies = new List<HeroPlayer>();
        private readonly List<HeroPlayer> _friendlies = new List<HeroPlayer>();
        private readonly PvPResolveTracker _resolveTracker = new PvPResolveTracker();
        private readonly HashSet<string> _loggedEffects = new HashSet<string>();
        private readonly HashSet<string> _loggedCasts = new HashSet<string>();
        private readonly SmartAoeCoordinator _chainLightningCoordinator;
        private Composite _combat;
        private Composite _rest;
        private Composite _pull;
        private HeroPlayer _target;
        private HeroPlayer _interruptTarget;
        private CombatMode _mode;
        private DateTime _lastTargetChangeUtc = DateTime.MinValue;
        private DateTime _lastStateLogUtc = DateTime.MinValue;
        private string _lastCapabilitySignature = string.Empty;

        public LightningPvP()
        {
            _chainLightningCoordinator = new SmartAoeCoordinator(new SmartAoeProfile
            {
                Key = "LightningPvP.ChainLightning",
                AbilityName = "Chain Lightning",
                Radius = Distance.MeleeAoE,
                MaxTargets = 8,
                TargetSelectionDwellSeconds = 0.4,
                PostCastDwellSeconds = 0.9,
                TransactionTimeoutSeconds = 3,
                Enabled = () => HasChainLightning,
                CandidateProvider = () => _enemies.Cast<HeroCharacter>(),
                CandidateFilter = IsUsableChainLightningTarget,
                SplashHazard = IsChainLightningSplashHazard,
                ImpactValue = ChainLightningImpact
            });
        }

        private static HeroPlayer Me => Core.Player;

        private int AttackersOnMe => _enemies.Count(enemy =>
            enemy.TargetId == Me.NodeId || Me.AttackerIds.Contains(enemy.NodeId));

        private bool HasAffliction => AbilityManager.HasAbility("Affliction");

        private bool HasThunderingBlast => AbilityManager.HasAbility("Thundering Blast");

        private bool HasLightningFlash => AbilityManager.HasAbility("Lightning Flash");

        private bool HasCrushingDarkness => AbilityManager.HasAbility("Crushing Darkness");

        private bool HasLightningBolt => AbilityManager.HasAbility("Lightning Bolt");

        private bool HasLightningStrike => AbilityManager.HasAbility("Lightning Strike");

        private bool HasForceLightning => AbilityManager.HasAbility("Force Lightning");

        private bool HasChainLightning => AbilityManager.HasAbility("Chain Lightning");

        private bool HasHaltedOffensive => AbilityManager.HasAbility("Halted Offensive");

        private bool HasVoltRush => AbilityManager.HasAbility("Volt Rush");

        private bool TargetHasAffliction =>
            _target != null && _target.HasMyDebuff("Affliction");

        private bool TargetIsCrushed =>
            _target != null &&
            (_target.HasMyDebuff("Crushing Darkness") ||
             _target.HasMyDebuff("Crushed (Crushing Darkness)"));

        private bool IsMovingOrFocused => Me.IsMoving || _mode == CombatMode.Focused;

        private bool BurstSetupReady =>
            !HasLightningFlash ||
            Me.HasBuff("Force Flash") ||
            (_target != null && _target.HasMyDebuff("Stormwatch"));

        private bool CanCommitBurst =>
            _target != null &&
            _target.HealthPercent > 20 &&
            _target.HealthPercent < 85 &&
            !LightningPvPEffects.HasBurstHold(_target) &&
            (!HasAffliction || TargetHasAffliction) &&
            BurstSetupReady &&
            AttackersOnMe <= 1;

        private bool ObjectiveEmergency =>
            _enemies.Any(enemy => LightningPvPEffects.IsObjectiveCast(enemy));

        private static bool PvPInterruptsAllowed =>
            !RoutineManager.IsAnyDisallowed(CapabilityFlags.Interrupting);

        public override void Initialize()
        {
            CombatHotkeys.Initialize();
            _chainLightningCoordinator.Reset();
            _resolveTracker.Reset();
            Logger.Write("PvP engine initialized: Sorcerer Lightning");

            _rest = new Decorator(
                ret => !Me.IsDead && !Me.IsMounted && !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    Spell.Buff("Mark of Power"),
                    Spell.HoT("Static Barrier", on => Me, 100),
                    Rest.HandleRest));

            _combat = new Decorator(
                ret => !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    new Action(ret => ScanPlayers()),
                    new Action(ret => HandlePriorityInterrupt()),
                    Spell.WaitForCast(),
                    RotationRuntime.MedPack.UseItem(ret => Me.HealthPercent <= 25),
                    DefensivePriority,
                    new Decorator(
                        ret => _chainLightningCoordinator.IsBusy,
                        new Action(ret => _chainLightningCoordinator.Continue())),
                    RecoveryPriority,
                    OffensiveCooldownPriority,
                    DamagePriority));

            _pull = _combat;
            CombatBehavior = _combat;
            RestBehavior = _rest;
            PullBehavior = _pull;
        }

        private Composite DefensivePriority => new PrioritySelector(
            Spell.Buff("Unbreakable Will",
                ret => ShouldBreakControl()),
            Spell.Buff("Force Barrier",
                ret => !Me.HasDebuff("Electro Net") &&
                       (Me.HealthPercent <= 18 ||
                        (Me.HealthPercent <= 32 && AttackersOnMe >= 2))),
            Spell.Buff("Unnatural Preservation",
                ret => Me.HealthPercent <= 62),
            Spell.Cast("Dark Heal", on => Me,
                ret => Me.HasBuff("Reserved Darkness") &&
                       Me.HealthPercent <= 58),
            Spell.Cast("Cloud Mind", on => Me,
                ret => Me.HealthPercent <= 72 && AttackersOnMe > 0),
            Spell.HoT("Static Barrier", on => Me, 100,
                ret => Me.HealthPercent <= 92 &&
                       !Me.HasDebuff("Deionized") &&
                       AttackersOnMe > 0),
            Spell.Cast("Overload", on => Me,
                ret => MeleeThreats().Count >= 2 &&
                       MeleeThreats().Any(CanKnockback)),
            Spell.Cast("Electrocute", ret => PrimaryThreat(),
                ret => _mode == CombatMode.Focused &&
                       PrimaryThreat() != null &&
                       CanHardStun(PrimaryThreat())));

        private Composite RecoveryPriority => new Decorator(
            ret => _mode == CombatMode.Recovery,
            new PrioritySelector(
                Spell.Cast("Resurgence", on => Me),
                Spell.Cast("Dark Heal", on => Me,
                    ret => !Me.IsMoving && AttackersOnMe == 0)));

        private Composite OffensiveCooldownPriority => new Decorator(
            ret => CanCommitBurst,
            new PrioritySelector(
                Spell.Cast("Polarity Shift", on => Me),
                Spell.Cast("Recklessness", on => Me)));

        private Composite DamagePriority => new Decorator(
            ret => IsValidEnemy(_target) && CanDamageTarget(_target),
            new PrioritySelector(
                Spell.Cast("Shock", ret => _target,
                    ret => LightningPvPEffects.IsObjectiveCast(_target) ||
                           _mode == CombatMode.Execute),
                Spell.DoT("Affliction", ret => _target, "Affliction", 0,
                    ret => HasAffliction &&
                           _target.HealthPercent >= 35 &&
                           !LightningPvPEffects.HasBurstHold(_target)),
                Spell.Cast("Lightning Flash", ret => _target,
                    ret => HasLightningFlash &&
                           !IsMovingOrFocused &&
                           _target.HealthPercent >= 30),
                Spell.Cast("Thundering Blast", ret => _target,
                    ret => HasThunderingBlast &&
                           !IsMovingOrFocused &&
                           (!HasAffliction || TargetHasAffliction)),
                new Action(ret => CastSmartChainLightning(
                    HasChainLightning &&
                    Me.HasBuff("Lightning Storm") &&
                    Me.HasBuff("Recklessness"))),
                Spell.Cast("Halted Offensive", ret => _target,
                    ret => HasHaltedOffensive &&
                           Me.HasBuff("Lightning Storm") &&
                           Me.HasBuff("Recklessness")),
                Spell.Cast("Crushing Darkness", ret => _target,
                    ret => HasCrushingDarkness &&
                           (_mode == CombatMode.Burst || _mode == CombatMode.Freecasting) &&
                           !Me.IsMoving &&
                           _target.HealthPercent >= 50 &&
                           !TargetIsCrushed &&
                           (Me.HasBuff("Force Flash") || !HasLightningFlash)),
                Spell.Cast("Shock", ret => _target,
                    ret => IsMovingOrFocused || TargetIsCrushed),
                new Action(ret => CastSmartChainLightning(
                    HasChainLightning && Me.HasBuff("Lightning Storm"))),
                Spell.Cast("Halted Offensive", ret => _target,
                    ret => HasHaltedOffensive && Me.HasBuff("Lightning Storm")),
                Spell.Cast("Volt Rush", ret => _target,
                    ret => HasVoltRush && Me.IsMoving),
                Spell.Cast("Lightning Bolt", ret => _target,
                    ret => HasLightningBolt && !IsMovingOrFocused),
                Spell.Cast("Lightning Strike", ret => _target,
                    ret => HasLightningStrike && !IsMovingOrFocused),
                Spell.Cast("Force Lightning", ret => _target,
                    ret => HasForceLightning &&
                           !Me.IsMoving &&
                           (_mode == CombatMode.Freecasting ||
                            (!HasLightningBolt && !HasLightningStrike))),
                Spell.Cast("Shock", ret => _target)));

        private RunStatus ScanPlayers()
        {
            _enemies.Clear();
            _friendlies.Clear();
            _resolveTracker.Track(Me);
            LogPlayerTelemetry(Me);

            foreach (var player in HeroObjectManager.GetObjectsOfType<HeroPlayer>())
            {
                if (player == null || player.NodeId == Me.NodeId || player.IsDead)
                    continue;

                _resolveTracker.Track(player);
                LogPlayerTelemetry(player);

                if (IsValidEnemy(player))
                    _enemies.Add(player);
                else if (player.GroupId != 0 && player.GroupId == Me.GroupId)
                    _friendlies.Add(player);
            }

            SelectTarget();
            SelectInterruptTarget();
            UpdateMode();
            LogState();
            LogCapabilities();
            return RunStatus.Failure;
        }

        private void SelectTarget()
        {
            var best = _enemies
                .OrderByDescending(TargetScore)
                .FirstOrDefault();

            if (best == null)
            {
                _target = null;
                return;
            }

            if (!IsValidEnemy(_target))
            {
                ChangeTarget(best);
                return;
            }

            if (best.NodeId == _target.NodeId)
                return;

            var currentScore = TargetScore(_target);
            var bestScore = TargetScore(best);
            var minimumLead = (DateTime.UtcNow - _lastTargetChangeUtc).TotalSeconds < 3 ? 28 : 16;
            if (bestScore >= currentScore + minimumLead ||
                LightningPvPEffects.HasBurstHold(_target))
                ChangeTarget(best);
        }

        private void ChangeTarget(HeroPlayer target)
        {
            _target = target;
            _lastTargetChangeUtc = DateTime.UtcNow;
        }

        private double TargetScore(HeroPlayer enemy)
        {
            var score = 100.0 - enemy.HealthPercent;
            score += LightningPvPEffects.IsObjectiveCast(enemy) ? 140 : 0;
            score += enemy.IsHealer() ? 28 : 0;
            score += LightningPvPEffects.IsPriorityCast(enemy) ? 38 : 0;
            score += enemy.TargetId == Me.NodeId ? 12 : 0;
            score += _friendlies.Count(ally => ally.TargetId == enemy.NodeId) * 8;
            score += enemy.HasMyDebuff("Affliction") ? 7 : 0;
            score += _target != null && enemy.NodeId == _target.NodeId ? 12 : 0;
            score -= Math.Max(0, enemy.Distance - 15) * 0.8;
            score -= LightningPvPEffects.HasDamageImmunity(enemy) ? 100 : 0;
            score -= LightningPvPEffects.HasDamageReturn(enemy) ? 90 : 0;
            score -= LightningPvPEffects.HasHealingDefensive(enemy) ? 55 : 0;
            score -= enemy.HasBuff("Guard") ? 15 : 0;
            return score;
        }

        private void SelectInterruptTarget()
        {
            _interruptTarget = _enemies
                .Where(enemy => LightningPvPEffects.IsPriorityCast(enemy))
                .OrderByDescending(enemy => LightningPvPEffects.IsObjectiveCast(enemy))
                .ThenByDescending(enemy => enemy.IsHealer())
                .ThenBy(enemy => enemy.CastTimeRemaining)
                .FirstOrDefault();
        }

        private RunStatus HandlePriorityInterrupt()
        {
            if (_interruptTarget == null ||
                !PvPInterruptsAllowed ||
                !AbilityManager.HasAbility("Jolt"))
            {
                return RunStatus.Failure;
            }

            if (Me.IsCasting)
            {
                AbilityManager.StopCasting(ablCancelReasonEnum.Manual);
                return RunStatus.Success;
            }

            if (!AbilityManager.CanCast("Jolt", _interruptTarget).Success)
                return RunStatus.Failure;

            var castName = _interruptTarget.CastingAbility != null
                ? _interruptTarget.CastingAbility.Name
                : "priority cast";
            var result = AbilityManager.Cast("Jolt", _interruptTarget);
            if (!result.Success)
                return RunStatus.Failure;

            Logger.Write("[PvP] Interrupting {0} on {1}", castName, _interruptTarget.Name);
            return RunStatus.Success;
        }

        private bool ShouldBreakControl()
        {
            if (Me.HasDebuff("Electro Net"))
                return Me.HealthPercent <= 28 && AttackersOnMe > 0;

            if (!Me.IsStunned && !LightningPvPEffects.HasControlEffect(Me))
                return false;

            if (ObjectiveEmergency)
                return true;

            var resolve = _resolveTracker.Estimate(Me);
            return Me.HealthPercent <= 22 && AttackersOnMe > 0 ||
                   Me.HealthPercent <= 35 && AttackersOnMe >= 2 && resolve >= 800;
        }

        private void UpdateMode()
        {
            if (Me.HealthPercent <= 38 && AttackersOnMe == 0)
                _mode = CombatMode.Recovery;
            else if (Me.HealthPercent <= 42 || AttackersOnMe >= 2)
                _mode = CombatMode.Focused;
            else if (_target != null && _target.HealthPercent <= 20)
                _mode = CombatMode.Execute;
            else if (CanCommitBurst)
                _mode = CombatMode.Burst;
            else if (AttackersOnMe == 0)
                _mode = CombatMode.Freecasting;
            else
                _mode = CombatMode.Pressure;
        }

        private List<HeroPlayer> MeleeThreats()
        {
            return _enemies
                .Where(enemy =>
                    enemy.Distance <= 5 &&
                    (enemy.TargetId == Me.NodeId || Me.AttackerIds.Contains(enemy.NodeId)))
                .ToList();
        }

        private HeroPlayer PrimaryThreat()
        {
            return _enemies
                .Where(enemy =>
                    enemy.TargetId == Me.NodeId || Me.AttackerIds.Contains(enemy.NodeId))
                .OrderBy(enemy => enemy.Distance)
                .FirstOrDefault();
        }

        private static bool IsValidEnemy(HeroPlayer enemy)
        {
            try
            {
                return enemy != null &&
                       enemy.IsHostile &&
                       enemy.IsTargetable &&
                       !enemy.IsDead &&
                       !enemy.IsStealthed &&
                       enemy.InLineOfSight &&
                       enemy.DistanceSqr <= Distance.RangedExt * Distance.RangedExt;
            }
            catch
            {
                return false;
            }
        }

        private bool CanDamageTarget(HeroCharacter enemy)
        {
            if (enemy == null || LightningPvPEffects.HasDamageImmunity(enemy))
                return false;

            if (LightningPvPEffects.IsObjectiveCast(enemy))
                return true;

            return !LightningPvPEffects.HasDamageReturn(enemy) &&
                   (!LightningPvPEffects.HasHealingDefensive(enemy) ||
                    enemy.HealthPercent <= 18);
        }

        private RunStatus CastSmartChainLightning(bool allowed)
        {
            return allowed
                ? _chainLightningCoordinator.Tick(1, _target)
                : RunStatus.Failure;
        }

        private bool IsUsableChainLightningTarget(HeroCharacter enemy)
        {
            var player = enemy as HeroPlayer;
            return IsValidEnemy(player) && CanDamageTarget(player) &&
                   !player.IsCrowdControlled();
        }

        private bool IsChainLightningSplashHazard(HeroCharacter enemy)
        {
            var player = enemy as HeroPlayer;
            return IsValidEnemy(player) &&
                   (player.IsCrowdControlled() ||
                    LightningPvPEffects.HasDamageImmunity(player) ||
                    LightningPvPEffects.HasDamageReturn(player));
        }

        private double ChainLightningImpact(HeroCharacter enemy)
        {
            var player = enemy as HeroPlayer;
            if (player == null)
                return 0;

            double value = 1;
            value += LightningPvPEffects.IsObjectiveCast(player) ? 0.75 : 0;
            value += player.IsHealer() ? 0.25 : 0;
            value += LightningPvPEffects.IsPriorityCast(player) ? 0.2 : 0;
            value += Math.Max(0, 100 - player.HealthPercent) / 500.0;
            return value;
        }

        private bool HasResolveImmunity(HeroCharacter enemy)
        {
            return enemy != null &&
                   (LightningPvPEffects.HasResolveImmunity(enemy) ||
                    _resolveTracker.IsWhiteBarred(enemy));
        }

        private bool CanHardStun(HeroCharacter enemy)
        {
            return enemy != null &&
                   !HasResolveImmunity(enemy) &&
                   _resolveTracker.Estimate(enemy) <=
                   PvPResolveTracker.MaximumAutomaticHardStunResolve;
        }

        private bool CanKnockback(HeroCharacter enemy)
        {
            return enemy != null &&
                   !HasResolveImmunity(enemy) &&
                   _resolveTracker.Estimate(enemy) <=
                   PvPResolveTracker.MaximumAutomaticKnockbackResolve;
        }

        private void LogPlayerTelemetry(HeroCharacter unit)
        {
            if (unit == null)
                return;

            foreach (var effect in unit.Buffs.Concat(unit.Debuffs))
            {
                if (effect == null || effect.IsPassive)
                    continue;

                LightningPvPEffects.Learn(effect);

                var duration = effect.Duration.TotalSeconds;
                if (duration <= 0 || duration > 30)
                    continue;

                var key = (effect.IsBuff ? "B" : "D") + ":" +
                          effect.AbilitySpecId.ToString("X16") + ":" +
                          (effect.Name ?? string.Empty);
                if (!_loggedEffects.Add(key))
                    continue;

                Logger.Write(
                    "[PvPEffect] slot={0}, effect={1}, spec=0x{2:X16}, duration={3:0.0}, owner={4}",
                    effect.IsBuff ? "buff" : "debuff",
                    effect.Name,
                    effect.AbilitySpecId,
                    duration,
                    unit.Name);
            }

            if (!unit.IsCasting || unit.CastingAbility == null)
                return;

            var castName = unit.CastingAbility.Name ?? string.Empty;
            var castKey = unit.CastingAbilitySpecId.ToString("X16") + ":" + castName;
            if (!_loggedCasts.Add(castKey))
                return;

            Logger.Write(
                "[PvPCast] ability={0}, spec=0x{1:X16}, total={2:0.0}, objective={3}, source={4}",
                castName,
                unit.CastingAbilitySpecId,
                unit.CastTimeTotal,
                LightningPvPEffects.IsObjectiveCast(unit),
                unit.Name);
        }

        private void LogState()
        {
            if ((DateTime.UtcNow - _lastStateLogUtc).TotalSeconds < 2)
                return;

            _lastStateLogUtc = DateTime.UtcNow;
            Logger.Write(
                "[PvP] mode={0}, target={1}, score={2:0.0}, resolve={3:0}, whitebar={4}, enemies={5}, attackers={6}, defensive={7}, objective={8}, interrupt={9}",
                _mode,
                _target != null ? _target.Name : "none",
                _target != null ? TargetScore(_target) : 0,
                _target != null ? _resolveTracker.Estimate(_target) : 0,
                _target != null && _resolveTracker.IsWhiteBarred(_target),
                _enemies.Count,
                AttackersOnMe,
                _target != null
                    ? LightningPvPEffects.ActiveDefensiveName(_target)
                    : string.Empty,
                _target != null && LightningPvPEffects.IsObjectiveCast(_target),
                _interruptTarget != null ? _interruptTarget.Name : "none");
        }

        private void LogCapabilities()
        {
            var signature =
                "Affliction=" + HasAffliction +
                ", ThunderingBlast=" + HasThunderingBlast +
                ", LightningFlash=" + HasLightningFlash +
                ", CrushingDarkness=" + HasCrushingDarkness +
                ", ChainLightning=" + HasChainLightning +
                ", HaltedOffensive=" + HasHaltedOffensive +
                ", LightningBolt=" + HasLightningBolt +
                ", LightningStrike=" + HasLightningStrike +
                ", ForceLightning=" + HasForceLightning +
                ", VoltRush=" + HasVoltRush;

            if (signature == _lastCapabilitySignature)
                return;

            _lastCapabilitySignature = signature;
            Logger.Write("[PvP] Adaptive abilities: {0}", signature);
        }
    }
}
