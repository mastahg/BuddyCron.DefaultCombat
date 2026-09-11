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
    ///     Sage Balance (DoT ranged dps) rotation: keeps Weaken Mind / Sever Force rolling,
    ///     Force in Balance on cooldown, and spends Presence of Mind on instant Vanquishes.
    /// </summary>
    public class Balance : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Balance;

        public override string Name => "Sage Balance";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Force Valor")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Force of Will", ret => Core.Player.IsStunned),

                    //Defensives
                    Spell.Buff("Force Barrier", ret => Core.Player.HealthPercent <= 20),
                    Spell.Buff("Force Mend", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Force Armor", ret => Core.Player.InCombat && !Core.Player.HasDebuff("Force-imbalanced")),

                    //Align throughput cooldowns with an established DoT window on durable targets.
                    Spell.Buff("Force Empowerment", ret => CombatHotkeys.EnableRaidBuffs),
                    Spell.Cast("Mental Alacrity",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (Core.Player.Target.HasMyDebuff("Weaken Mind") || !AbilityManager.HasAbility("Weaken Mind"))),
                    Spell.Cast("Force Potency",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (Core.Player.Target.HasMyDebuff("Weaken Mind") || !AbilityManager.HasAbility("Weaken Mind"))),

                    //Force management
                    Spell.Cast("Vindicate", ret => Core.Player.ForcePercent < 50 && Core.Player.HealthPercent > 50 && !Core.Player.HasDebuff("Weary")),

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

                    //Rotation (7.x priority: DoTs > Force in Balance > Vanquish > Force Serenity,
                    //          Telekinetic Throw builds Presence of Mind to make Vanquish/Disturbance instant)
                    Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Presence of Mind (caps at 4 stacks) -> instant, cheaper, harder-hitting Vanquish
                    Spell.Cast("Vanquish", ret => Core.Player.BuffCount("Presence of Mind") >= 4),

                    //DoTs first -- Force in Balance and Force Serenity are boosted by them
                    Spell.DoT("Weaken Mind", "Weaken Mind"),
                    Spell.DoT("Sever Force", "Sever Force"),

                    //Force in Balance on cooldown (ground-targeted sphere, applies Force Suppression)
                    Spell.CastOnGround("Force in Balance"),

                    //Force Serenity wants Weaken Mind on the target for its damage bonus
                    Spell.Cast("Force Serenity", ret => Core.Player.Target.HasMyDebuff("Weaken Mind") || Core.Player.Level < 30),

                    //Vanquish on cooldown even without the proc
                    Spell.Cast("Vanquish"),

                    //Dump leftover Presence of Mind stacks into an instant Disturbance
                    Spell.Cast("Disturbance", ret => Core.Player.BuffCount("Presence of Mind") >= 4),

                    //Telekinetic Blitz is a movement fallback unless a tactical-specific AoE policy owns it.
                    Spell.Cast("Telekinetic Blitz", ret => Core.Player.IsMoving),

                    //Fillers -- Telekinetic Throw is the builder/filler channel; Disturbance, Project and
                    //Saber Strike keep low-level characters from ever stalling
                    Spell.Cast("Telekinetic Throw", ret => Core.Player.BuffCount("Presence of Mind") < 4),
                    Spell.Cast("Disturbance"),
                    Spell.Cast("Project"),
                    Spell.Cast("Saber Strike", ret => Core.Player.Target.EdgeDistance <= Distance.Melee)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                        //Keep the DoTs rolling -- Force in Balance spreads/benefits from them
                        Spell.DoT("Weaken Mind", "Weaken Mind"),
                        Spell.DoT("Sever Force", "Sever Force"),
                        Spell.CastOnGround("Force in Balance"),

                        Spell.Cast("Vanquish", ret => Core.Player.BuffCount("Presence of Mind") >= 4),
                        Spell.CastOnGround("Forcequake")
                        ));
            }
        }
    }
}
