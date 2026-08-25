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
    ///     7.x Sorcerer Lightning (ranged burst DPS) rotation, built around the Affliction
    ///     auto-crit setup for Thundering Blast and the Lightning Storm / Force Flash procs.
    /// </summary>
    public class Lightning : RotationBase
    {
        private const double CrushingDarknessMinimumTtd = 8;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Lightning;

        public override string Name => "Sorcerer Lightning";

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

                    //Start Polarity Shift before applying the long-lived DoT. Hold the charge-based
                    //cooldowns for Lightning Flash's Force Flash window instead of wasting them on setup.
                    Spell.Cast("Polarity Shift",
                        ret => Core.Player.InCombat && Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (!Core.Player.Target.HasMyDebuff("Affliction") || Core.Player.HasBuff("Force Flash"))),
                    Spell.Cast("Recklessness",
                        ret => Core.Player.HasBuff("Force Flash") || !AbilityManager.HasAbility("Lightning Flash")),
                    Spell.Buff("Force Speed",
                        ret => Core.Player.HasBuff("Force Flash") || !AbilityManager.HasAbility("Lightning Flash")),
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
                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt
                    Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Affliction must be up: it makes Thundering Blast an automatic crit
                    Spell.DoT("Affliction", "Affliction"),

                    //Rotation
                    Spell.Cast("Thundering Blast"),
                    Spell.Cast("Lightning Flash"),   // grants Force Flash + Stormwatch
                    Spell.Cast("Crushing Darkness",
                        ret => (Core.Player.HasBuff("Force Flash") || Core.Player.Level < 50) &&
                               Core.Player.Target.WillLiveFor(CrushingDarknessMinimumTtd)),
                    Spell.Cast("Shock", ret => Core.Player.Target.HasMyDebuff("Crushed (Crushing Darkness)") || Core.Player.Level < 26),
                    Spell.Cast("Chain Lightning", ret => Core.Player.HasBuff("Lightning Storm")),
                    // lvl 23 choice (apc.sith_inquisitor.sorcerer.lightning_mods), replaces Chain Lightning.
                    // NB: the client's name string is "Halted Offensive " WITH a trailing space, so
                    // AbilityManager matches ability names whitespace-insensitively.
                    Spell.Cast("Halted Offensive", ret => Core.Player.HasBuff("Lightning Storm")),
                    //Volt Rush is a movement fallback unless a tactical-specific AoE policy owns it.
                    Spell.Cast("Volt Rush", ret => Core.Player.IsMoving),

                    //Fillers
                    Spell.Cast("Lightning Bolt"),
                    Spell.Cast("Lightning Strike"),   // pre-Lightning-Bolt filler on low level characters
                    Spell.Cast("Saber Strike", ret => Core.Player.ForcePercent <= 30)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.Cast("Chain Lightning", ret => Core.Player.HasBuff("Lightning Storm")),
                        Spell.Cast("Halted Offensive", ret => Core.Player.HasBuff("Lightning Storm")),
                        Spell.DoT("Affliction", "Affliction"),
                        Spell.Cast("Chain Lightning"),
                        Spell.Cast("Halted Offensive"),
                        Spell.CastOnGround("Force Storm")
                        ));
            }
        }
    }
}
