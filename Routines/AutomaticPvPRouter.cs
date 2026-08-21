using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Inheritables;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Routines
{
    internal sealed class AutomaticPvPContext
    {
        private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan HostileContactGrace = TimeSpan.FromSeconds(15);
        private static readonly HashSet<string> DedicatedPvPAreas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ald_pvp",
                "pvp_arena"
            };

        private DateTime _lastEvaluationUtc = DateTime.MinValue;
        private DateTime _lastHostileContactUtc = DateTime.MinValue;
        private ulong _lastLoadedAreaId;
        private string _lastLoadedAreaName = string.Empty;
        private bool _lastLoadedAreaWasPvP;
        private bool _initialized;
        private bool _usePvP;

        internal bool UsePvP
        {
            get
            {
                Evaluate();
                return _usePvP;
            }
        }

        private void Evaluate()
        {
            var now = DateTime.UtcNow;
            if (now - _lastEvaluationUtc < EvaluationInterval)
                return;

            _lastEvaluationUtc = now;
            var client = HeroBaseClient.Instance;
            if (client == null || !client.AreaLoaded)
                return;

            var areaName = client.AreaName ?? string.Empty;
            var areaIsPvP = IsDedicatedPvPArea(areaName);
            var areaChanged = client.AreaId != _lastLoadedAreaId ||
                              !string.Equals(areaName, _lastLoadedAreaName,
                                  StringComparison.OrdinalIgnoreCase);
            var leftDedicatedPvP = areaChanged && _lastLoadedAreaWasPvP && !areaIsPvP;

            if (areaChanged)
            {
                _lastLoadedAreaId = client.AreaId;
                _lastLoadedAreaName = areaName;
                _lastLoadedAreaWasPvP = areaIsPvP;
            }

            if (leftDedicatedPvP)
            {
                _lastHostileContactUtc = DateTime.MinValue;
                SetMode(false, "loaded into a non-PvP area", areaName, 0);
                return;
            }

            if (areaIsPvP)
            {
                SetMode(true, "dedicated PvP instance", areaName, 0);
                return;
            }

            var hostileContacts = CountHostilePlayerContacts();
            if (hostileContacts > 0)
            {
                _lastHostileContactUtc = now;
                SetMode(true, "hostile player combat", areaName, hostileContacts);
                return;
            }

            if (_lastHostileContactUtc != DateTime.MinValue &&
                now - _lastHostileContactUtc <= HostileContactGrace)
            {
                SetMode(true, "hostile-player handoff grace", areaName, 0);
                return;
            }

            SetMode(false, "no PvP context", areaName, 0);
        }

        private void SetMode(bool usePvP, string reason, string areaName, int hostileContacts)
        {
            if (_initialized && _usePvP == usePvP)
                return;

            _initialized = true;
            _usePvP = usePvP;
            var mode = usePvP ? "PvP takeover" : "PvE restored";
            Logger.Write(string.Format(
                "[AutoPvP] {0}: {1}; area={2}; hostileContacts={3}",
                mode,
                reason,
                string.IsNullOrWhiteSpace(areaName) ? "unknown" : areaName,
                hostileContacts));
        }

        private static bool IsDedicatedPvPArea(string areaName)
        {
            if (string.IsNullOrWhiteSpace(areaName))
                return false;

            return DedicatedPvPAreas.Contains(areaName) ||
                   areaName.StartsWith("pvp_", StringComparison.OrdinalIgnoreCase) ||
                   areaName.IndexOf("_warzone_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   areaName.IndexOf("_arena_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountHostilePlayerContacts()
        {
            try
            {
                var me = Core.Player;
                if (me == null || !me.IsValid)
                    return 0;

                var players = HeroObjectManager.GetObjectsOfType<HeroPlayer>()
                    .Where(player => player != null && player.IsValid && !player.IsDead)
                    .ToList();
                var enemies = players
                    .Where(player => !player.IsLocalPlayer && player.IsHostile)
                    .ToList();
                var allies = me.GroupId == 0
                    ? new List<HeroPlayer>()
                    : players.Where(player =>
                            !player.IsLocalPlayer &&
                            player.GroupId == me.GroupId &&
                            !player.IsHostile)
                        .ToList();

                return enemies.Count(enemy =>
                    HasCombatContact(me, enemy) ||
                    allies.Any(ally => HasCombatContact(ally, enemy)));
            }
            catch
            {
                return 0;
            }
        }

        private static bool HasCombatContact(HeroCharacter friendly, HeroPlayer enemy)
        {
            if (friendly == null || enemy == null)
                return false;

            return friendly.IsInCombatWith(enemy) ||
                   enemy.IsInCombatWith(friendly) ||
                   friendly.AttackerIds.Contains(enemy.NodeId) ||
                   enemy.AttackerIds.Contains(friendly.NodeId) ||
                   enemy.TargetId == friendly.NodeId && enemy.InCombat ||
                   friendly.TargetId == enemy.NodeId && friendly.InCombat;
        }
    }

    public abstract class AutomaticRoutineEngine
    {
        public Composite CombatBehavior { get; protected set; }

        public Composite CombatBuffBehavior { get; protected set; }

        public Composite DeathBehavior { get; protected set; }

        public Composite HealBehavior { get; protected set; }

        public Composite PreCombatBuffBehavior { get; protected set; }

        public Composite PullBehavior { get; protected set; }

        public Composite PullBuffBehavior { get; protected set; }

        public Composite RestBehavior { get; protected set; }

        public abstract void Initialize();

        public virtual void Pulse()
        {
        }

        public virtual void ShutDown()
        {
        }
    }

    internal abstract class RotationEngineBase : AutomaticRoutineEngine
    {
        private DateTime _lastCombatIdleLogUtc = DateTime.MinValue;

        public abstract string Name { get; }

        public abstract CharacterDiscipline Discipline { get; }

        public abstract Composite Buffs { get; }

        public abstract Composite Cooldowns { get; }

        public abstract Composite SingleTarget { get; }

        public abstract Composite AreaOfEffect { get; }

        public sealed override void Initialize()
        {
            Logger.Write("*** Default Combat v90***");
            Logger.Write("Level: " + Core.Player.Level);
            Logger.Write("Class: " + Core.Player.CharacterClass);
            Logger.Write("Discipline: " + Core.Player.CharacterDiscipline);

            CombatHotkeys.Initialize();
            Logger.Write("Rotation Selected : " + Name);

            if (RotationRuntime.IsHealer)
                Logger.Write("Healing Enabled");

            RestBehavior = new Decorator(
                ret => !Core.Player.IsDead && !Core.Player.IsMounted && !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    Targeting.ScanTargets,
                    new Decorator(ret => RotationRuntime.IsHealer, AreaOfEffect),
                    Spell.Buff(Core.Player.SelfBuffName()),
                    Buffs,
                    Rest.HandleRest,
                    Scavenge.ScavengeCorpse));

            CombatBehavior = new Decorator(
                ret => !CombatHotkeys.PauseRotation,
                new PrioritySelector(
                    Spell.WaitForCast(),
                    RotationRuntime.MedPack.UseItem(ret => Core.Player.HealthPercent <= 30),
                    Targeting.ScanTargets,
                    Cooldowns,
                    new Decorator(ret => RotationRuntime.IsHealer || CombatHotkeys.EnableAoe, AreaOfEffect),
                    SingleTarget,
                    new Action(ret => LogCombatIdleState())));

            PullBehavior = new Decorator(
                ret => !CombatHotkeys.PauseRotation &&
                       (!RotationRuntime.MovementDisabled || RotationRuntime.IsHealer && !RotationRuntime.Grind),
                CombatBehavior);
        }

        private RunStatus LogCombatIdleState()
        {
            if (Core.Player == null || !Core.Player.InCombat ||
                (DateTime.UtcNow - _lastCombatIdleLogUtc).TotalSeconds < 1.5)
            {
                return RunStatus.Failure;
            }

            _lastCombatIdleLogUtc = DateTime.UtcNow;
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
            catch (Exception exception)
            {
                Logger.Write("[CombatIdle] state read failed: " + exception.Message);
            }

            return RunStatus.Failure;
        }
    }

    public abstract class AutomaticPvPRoutine : CombatRoutine
    {
        private readonly AutomaticPvPContext _context = new AutomaticPvPContext();
        private AutomaticRoutineEngine _pve;
        private AutomaticRoutineEngine _pvp;
        private Composite _pull;

        public abstract CharacterDiscipline Discipline { get; }

        public sealed override CharacterDiscipline[] Class => new[] { Discipline };

        public sealed override float PullRange => 3f;

        public sealed override Composite PullBehavior => _pull;

        protected abstract AutomaticRoutineEngine CreatePvE();

        protected abstract AutomaticRoutineEngine CreatePvP();

        public sealed override void Initialize()
        {
            _pve = CreatePvE();
            _pvp = CreatePvP();
            _pve.Initialize();
            _pvp.Initialize();

            CombatBehavior = Route(_pve.CombatBehavior, _pvp.CombatBehavior);
            RestBehavior = Route(_pve.RestBehavior, _pvp.RestBehavior);
            _pull = Route(_pve.PullBehavior, _pvp.PullBehavior);
            CombatBuffBehavior = Route(_pve.CombatBuffBehavior, _pvp.CombatBuffBehavior);
            DeathBehavior = Route(_pve.DeathBehavior, _pvp.DeathBehavior);
            HealBehavior = Route(_pve.HealBehavior, _pvp.HealBehavior);
            PreCombatBuffBehavior = Route(_pve.PreCombatBuffBehavior, _pvp.PreCombatBuffBehavior);
            PullBuffBehavior = Route(_pve.PullBuffBehavior, _pvp.PullBuffBehavior);

            Logger.Write("Automatic PvE/PvP routing enabled for " + Name);
        }

        public sealed override void Pulse()
        {
            if (_context.UsePvP)
                _pvp.Pulse();
            else
                _pve.Pulse();
        }

        public sealed override void ShutDown()
        {
            try
            {
                _pvp.ShutDown();
            }
            finally
            {
                _pve.ShutDown();
            }
        }

        private Composite Route(Composite pve, Composite pvp)
        {
            if (pve == null && pvp == null)
                return null;

            return new PrioritySelector(
                new Decorator(
                    ret => _context.UsePvP,
                    pvp ?? Failure()),
                new Decorator(
                    ret => !_context.UsePvP,
                    pve ?? Failure()));
        }

        private static Composite Failure()
        {
            return new Action(ret => RunStatus.Failure);
        }
    }

    public sealed class Lightning : AutomaticPvPRoutine
    {
        public override string Name => "Sorcerer Lightning";

        public override CharacterDiscipline Discipline => CharacterDiscipline.Lightning;

        protected override AutomaticRoutineEngine CreatePvE()
        {
            return new LightningPvE();
        }

        protected override AutomaticRoutineEngine CreatePvP()
        {
            return new LightningPvP();
        }
    }
}
