// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using BuddyCron.Managers;
using DefaultCombat.Behaviors;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Helpers
{
    /// <summary>Provides shared player, bot-mode, item, and behavior state to the discipline routines.</summary>
    internal static class RotationRuntime
    {
        /// <summary>The shared medpac wrapper and its reuse timer.</summary>
        internal static readonly InventoryManagerItem MedPack = new InventoryManagerItem("Medpac", 90);

        /// <summary>Legacy Heroic Moment priority shared by every discipline.</summary>
        internal static readonly Composite HeroicMoment = new Decorator(
            ret => Core.Player.HasBuff("Heroic Moment"),
            new PrioritySelector(
                //Self-cast 5 m sphere around us.
                Spell.Cast("Legacy Force Sweep", ret => Core.Player.Target.Distance <= 0.5f),
                //Ground AoE placed on the target, castable up to 30 m away.
                Spell.CastOnGround("Legacy Orbital Strike", ret => Core.Player.Target.Distance <= 3f),
                Spell.Cast("Legacy Project"),
                Spell.Cast("Legacy Dirty Kick", ret => Core.Player.Target.EdgeDistance < .5f),
                Spell.Cast("Legacy Sticky Plasma Grenade"),
                //Self-cast 10 m / 45 degree forward cone, never range-checked by the engine.
                Spell.Cast("Legacy Flame Thrower", ret => Targeting.InFrontalCone(Core.Player.Target, 1f, 45f)),
                Spell.Cast("Legacy Force Lightning"),
                Spell.Cast("Legacy Force Choke")));

        /// <summary>True when the active discipline is a healing discipline.</summary>
        internal static bool IsHealer => Core.Player.IsHealer();

        /// <summary>True when the current botbase is not autonomous.</summary>
        internal static bool MovementDisabled => !BotManager.Current.IsAutonomous;

        /// <summary>True when the current botbase runs autonomously.</summary>
        internal static bool Grind => BotManager.Current.IsAutonomous;
    }
}
