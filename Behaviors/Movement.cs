// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using BuddyCron;
using BuddyCron.Behaviors;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Behaviors
{
    /// <summary>Movement composites shared by rotations.</summary>
    public class CombatMovement
    {
        // MoveIntoRange stops on center distance, but the game's range gate subtracts the larger of
        // the two hit radii (HeroCharacter.EdgeDistance), so a plain center-distance stop walks
        // under anything bigger than a humanoid (a bantha's radius is 0.36 against a 0.4 melee
        // range). Widening the stop range by that radius makes the center stop land exactly where
        // EdgeDistance == range.
        /// <summary>Creates a composite that closes on the current target until it is within
        /// <paramref name="range"/> as the game measures ability range (from the target's hit
        /// edge, see <see cref="HeroCharacter.EdgeDistance"/>) and in line of sight, while the
        /// botbase is autonomous. Blocks later rotation actions until then.</summary>
        public static Composite CloseDistance(float range)
        {
            return new Decorator(
                ret => !RotationRuntime.MovementDisabled,
                CommonBehaviors.MoveIntoRange(
                    ret => Core.Player.Target,
                    ret => range + Math.Max(Core.Player.MeleeDistance, Core.Player.Target.MeleeDistance),
                    false,
                    ret => $"Closing to {range} on {Core.Player.Target.Name}"));
        }
    }
}
