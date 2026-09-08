// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using BuddyCron.Behaviors;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Behaviors
{
    /// <summary>Movement composites shared by rotations.</summary>
    public class CombatMovement
    {
        //public static Composite CloseDistance(float range)
        //{
        //    return new Decorator(ret => !RotationRuntime.MovementDisabled && Core.Player.Target != null,
        //        new PrioritySelector(
        //            new Decorator(ret => Core.Player.Target.Distance < range,
        //                new Action(delegate
        //                {
        //                    Navigator.Stop();
        //                    return RunStatus.Failure;
        //                })),
        //            new Decorator(ret => Core.Player.Target.Distance >= range,
        //                CommonBehaviors.MoveAndStop(location => Core.Player.Target.Location, range, true)),
        //            new Action(delegate { return RunStatus.Failure; })));
        //}



        /// <summary>Creates a composite that closes to <paramref name="range"/> while the botbase
        /// is autonomous, blocking later rotation actions until the current target is in range.</summary>
        public static Composite CloseDistance(float range)
        {
            return new Decorator(
                ret => !RotationRuntime.MovementDisabled,
                CommonBehaviors.MoveIntoRange(
                    location => Core.Player.Target,
                    ret => range,
                    false,
                    ret => $"Closing to {range} on {Core.Player.Target.Name}"));
        }
    }
}
