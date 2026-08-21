using System;
using BuddyCron;
using BuddyCron.Objects;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Behaviors
{
    internal sealed class TargetHandoffCastAttempt
    {
        internal bool Accepted { get; set; }

        internal string FailureDetail { get; set; }
    }

    internal sealed class TargetHandoffRequest
    {
        internal string LogPrefix { get; set; }

        internal string ProfileKey { get; set; }

        internal HeroCharacter StartingTarget { get; set; }

        internal HeroCharacter RestoreTarget { get; set; }

        internal HeroCharacter PendingTarget { get; set; }

        internal double TargetSelectionDwellSeconds { get; set; }

        internal double PostCastDwellSeconds { get; set; }

        internal double TransactionTimeoutSeconds { get; set; }

        internal Func<bool> CanContinue { get; set; }

        internal Func<ulong, HeroCharacter> ResolveTarget { get; set; }

        internal Func<HeroCharacter, bool> IsPendingUsable { get; set; }

        internal Func<HeroCharacter, bool> IsRestorable { get; set; }

        internal Func<HeroCharacter, TargetHandoffCastAttempt> TryCast { get; set; }

        internal Action<HeroCharacter, string> OnCastTimeout { get; set; }
    }

    internal sealed class TargetHandoffCoordinator
    {
        private enum HandoffState
        {
            Idle,
            AwaitingTarget,
            AwaitingCast,
            HoldingTarget,
            AwaitingRestore
        }

        private TargetHandoffRequest _request;
        private HandoffState _state;
        private DateTime _transactionStartedUtc = DateTime.MinValue;
        private DateTime _stateStartedUtc = DateTime.MinValue;
        private DateTime _targetObservedUtc = DateTime.MinValue;
        private DateTime _lastTargetCommandUtc = DateTime.MinValue;
        private string _lastCastFailure = string.Empty;

        internal bool IsBusy => _state != HandoffState.Idle;

        internal void Begin(TargetHandoffRequest request)
        {
            if (request == null || request.PendingTarget == null)
                return;

            _request = request;
            _state = HandoffState.AwaitingTarget;
            _transactionStartedUtc = DateTime.UtcNow;
            _stateStartedUtc = _transactionStartedUtc;
            _targetObservedUtc = DateTime.MinValue;
            _lastTargetCommandUtc = DateTime.MinValue;
            _lastCastFailure = string.Empty;
            Logger.Write(string.Format(
                "[{0}] Starting target handoff; profile={1} selected=0x{2:X} restore=0x{3:X} pending={4} id=0x{5:X}",
                Prefix, ProfileKey, IdOf(request.StartingTarget), IdOf(request.RestoreTarget),
                SafeName(request.PendingTarget), request.PendingTarget.NodeId));
            IssueTargetCommand(request.PendingTarget, _transactionStartedUtc);
        }

        internal RunStatus Continue()
        {
            if (!IsBusy || _request == null)
                return RunStatus.Failure;

            if (!CanContinue())
            {
                Reset();
                return RunStatus.Failure;
            }

            var now = DateTime.UtcNow;
            var current = Core.Player != null ? Core.Player.Target : null;
            var pending = Resolve(_request.PendingTarget);

            if (_state != HandoffState.AwaitingRestore && !IsPendingUsable(pending))
                return RestoreOrFinish("pending target unavailable");

            if (current != null && !IsExpectedTarget(current))
            {
                Logger.Write(string.Format(
                    "[{0}] Handoff cancelled by target change; profile={1}", Prefix, ProfileKey));
                Reset();
                return RunStatus.Failure;
            }

            if ((now - _transactionStartedUtc).TotalSeconds >= TimeoutSeconds &&
                _state != HandoffState.HoldingTarget &&
                _state != HandoffState.AwaitingRestore)
            {
                return RestoreOrFinish("transaction timeout");
            }

            if (_state == HandoffState.AwaitingTarget)
                return AwaitTarget(current, pending, now);

            if (_state == HandoffState.AwaitingCast)
                return AwaitCast(current, pending, now);

            if (_state == HandoffState.HoldingTarget)
            {
                if ((now - _stateStartedUtc).TotalSeconds <
                    Math.Max(0, _request.PostCastDwellSeconds))
                {
                    return RunStatus.Success;
                }

                return RestoreOrFinish("cast dwell complete");
            }

            if (_state == HandoffState.AwaitingRestore)
                return AwaitRestore(current, now);

            Reset();
            return RunStatus.Failure;
        }

        internal void Reset()
        {
            _request = null;
            _state = HandoffState.Idle;
            _transactionStartedUtc = DateTime.MinValue;
            _stateStartedUtc = DateTime.MinValue;
            _targetObservedUtc = DateTime.MinValue;
            _lastTargetCommandUtc = DateTime.MinValue;
            _lastCastFailure = string.Empty;
        }

        private RunStatus AwaitTarget(HeroCharacter current, HeroCharacter pending, DateTime now)
        {
            if (!Matches(current, pending))
            {
                IssueTargetCommand(pending, now);
                return RunStatus.Success;
            }

            if (_targetObservedUtc == DateTime.MinValue)
            {
                _targetObservedUtc = now;
                Logger.Write(string.Format(
                    "[{0}] Target observed; profile={1} target={2} id=0x{3:X}",
                    Prefix, ProfileKey, SafeName(pending), pending.NodeId));
                return RunStatus.Success;
            }

            if ((now - _targetObservedUtc).TotalSeconds <
                Math.Max(0, _request.TargetSelectionDwellSeconds))
            {
                return RunStatus.Success;
            }

            _state = HandoffState.AwaitingCast;
            _stateStartedUtc = now;
            return RunStatus.Success;
        }

        private RunStatus AwaitCast(HeroCharacter current, HeroCharacter pending, DateTime now)
        {
            if (!Matches(current, pending))
            {
                _state = HandoffState.AwaitingTarget;
                _targetObservedUtc = DateTime.MinValue;
                _stateStartedUtc = now;
                return RunStatus.Success;
            }

            TargetHandoffCastAttempt attempt;
            try
            {
                attempt = _request.TryCast != null ? _request.TryCast(pending) : null;
            }
            catch (Exception exception)
            {
                attempt = new TargetHandoffCastAttempt
                {
                    FailureDetail = exception.GetType().Name
                };
            }

            if (attempt != null && attempt.Accepted)
            {
                _state = HandoffState.HoldingTarget;
                _stateStartedUtc = now;
                return RunStatus.Success;
            }

            if (attempt != null && !string.IsNullOrWhiteSpace(attempt.FailureDetail))
                _lastCastFailure = attempt.FailureDetail;

            if ((now - _stateStartedUtc).TotalSeconds < TimeoutSeconds)
                return RunStatus.Success;

            if (_request.OnCastTimeout != null)
                _request.OnCastTimeout(pending, _lastCastFailure);
            return RestoreOrFinish("cast timeout");
        }

        private RunStatus AwaitRestore(HeroCharacter current, DateTime now)
        {
            var restore = Resolve(_request.RestoreTarget);
            if (Matches(current, restore))
            {
                Logger.Write(string.Format(
                    "[{0}] Original target restored; profile={1} id=0x{2:X}",
                    Prefix, ProfileKey, restore.NodeId));
                Reset();
                return RunStatus.Failure;
            }

            if ((now - _stateStartedUtc).TotalSeconds >= TimeoutSeconds ||
                !IsRestorable(restore))
            {
                Reset();
                return RunStatus.Failure;
            }

            IssueTargetCommand(restore, now);
            return RunStatus.Success;
        }

        private RunStatus RestoreOrFinish(string reason)
        {
            var current = Core.Player != null ? Core.Player.Target : null;
            var restore = Resolve(_request.RestoreTarget);
            var pending = Resolve(_request.PendingTarget);

            if (restore == null || Matches(restore, pending) || !IsRestorable(restore) ||
                current != null && !IsExpectedTarget(current))
            {
                Logger.Write(string.Format(
                    "[{0}] Handoff finished without restore; profile={1} reason={2}",
                    Prefix, ProfileKey, reason));
                Reset();
                return RunStatus.Failure;
            }

            if (Matches(current, restore))
            {
                Reset();
                return RunStatus.Failure;
            }

            _state = HandoffState.AwaitingRestore;
            _stateStartedUtc = DateTime.UtcNow;
            _lastTargetCommandUtc = DateTime.MinValue;
            Logger.Write(string.Format(
                "[{0}] Restoring original target; profile={1} target={2} id=0x{3:X} reason={4}",
                Prefix, ProfileKey, SafeName(restore), restore.NodeId, reason));
            IssueTargetCommand(restore, DateTime.UtcNow);
            return RunStatus.Success;
        }

        private bool CanContinue()
        {
            try
            {
                return _request.CanContinue == null || _request.CanContinue();
            }
            catch
            {
                return false;
            }
        }

        private bool IsPendingUsable(HeroCharacter target)
        {
            try
            {
                return _request.IsPendingUsable != null && _request.IsPendingUsable(target);
            }
            catch
            {
                return false;
            }
        }

        private bool IsRestorable(HeroCharacter target)
        {
            try
            {
                return _request.IsRestorable != null && _request.IsRestorable(target);
            }
            catch
            {
                return false;
            }
        }

        private HeroCharacter Resolve(HeroCharacter fallback)
        {
            if (fallback == null)
                return null;

            try
            {
                var resolved = _request.ResolveTarget != null
                    ? _request.ResolveTarget(fallback.NodeId)
                    : null;
                return resolved ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private bool IsExpectedTarget(HeroCharacter target)
        {
            return Matches(target, _request.StartingTarget) ||
                   Matches(target, _request.RestoreTarget) ||
                   Matches(target, _request.PendingTarget);
        }

        private void IssueTargetCommand(HeroCharacter target, DateTime now)
        {
            if (target == null || (now - _lastTargetCommandUtc).TotalSeconds < 0.25)
                return;

            target.SetTarget();
            _lastTargetCommandUtc = now;
        }

        private string Prefix => !string.IsNullOrWhiteSpace(_request.LogPrefix)
            ? _request.LogPrefix
            : "TargetHandoff";

        private string ProfileKey => !string.IsNullOrWhiteSpace(_request.ProfileKey)
            ? _request.ProfileKey
            : "unknown";

        private double TimeoutSeconds => Math.Max(0.5, _request.TransactionTimeoutSeconds);

        private static ulong IdOf(HeroCharacter target)
        {
            return target != null ? target.NodeId : 0;
        }

        private static bool Matches(HeroCharacter left, HeroCharacter right)
        {
            return left != null && right != null && left.NodeId == right.NodeId;
        }

        private static string SafeName(HeroCharacter target)
        {
            try
            {
                return target != null && !string.IsNullOrWhiteSpace(target.Name)
                    ? target.Name
                    : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
