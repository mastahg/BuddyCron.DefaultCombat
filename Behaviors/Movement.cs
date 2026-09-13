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
        // The game's range gate is 3D center distance minus the larger of the two hit radii
        // (HeroCharacter.EdgeDistance). MoveIntoRange stops on Navigator.InPosition, an XZ-plane
        // distance with a 4.5 Y tolerance, so on any slope or against a tall target (whose position
        // sits above our feet) the planar distance undershoots the 3D one and the mover declares
        // arrival a fraction outside the cast gate — every Spell.Cast then fails out of range and
        // the rotation stalls. Stopping on EdgeDistance itself makes the stop and the cast gate the
        // same predicate, so they cannot disagree.
        /// <summary>Creates a composite that closes on the current target until it is within
        /// <paramref name="range"/> as the game measures ability range (from the target's hit
        /// edge, see <see cref="HeroCharacter.EdgeDistance"/>) and in line of sight, while the
        /// botbase is autonomous. Blocks later rotation actions until then.</summary>
        public static Composite CloseDistance(float range)
        {
            return new Decorator(
                ret => !RotationRuntime.MovementDisabled,
                CommonBehaviors.MoveAndStop(
                    ret => Core.Player.Target.Location,
                    ret => Core.Player.Target.EdgeDistance <= range && Core.Player.Target.InLineOfSight,
                    ret => $"Closing to {range} on {Core.Player.Target.Name}"));
        }
    }
}
