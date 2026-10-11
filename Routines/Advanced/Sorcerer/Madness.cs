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
    ///     7.x Sorcerer Madness (ranged DoT DPS) rotation: keeps Affliction and Creeping Terror
    ///     up, spreads them with Death Field and spends 4-stack Wrath on Demolish.
    /// </summary>
    public class Madness : RotationBase
    {
        // Death Field (abl.sith_inquisitor.skill.madness.death_field): Target type Ground, Range up to
        // 10 m, AoE Sphere radius 5 m around the placed point. No Sorcerer passive extends the range.
        private const float DeathFieldRange = 1.0f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Madness;

        public override string Name => "Sorcerer Madness";


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

                    //Align throughput cooldowns with an established DoT window on durable targets.
                    Spell.Cast("Polarity Shift",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (Core.Player.Target.HasMyDebuff("Affliction") || !AbilityManager.HasAbility("Affliction"))),
                    Spell.Cast("Recklessness",
                        ret => Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (Core.Player.Target.HasMyDebuff("Affliction") || !AbilityManager.HasAbility("Affliction"))),
                    Spell.Buff("Force Speed", ret => Core.Player.IsMoving),
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

                    //Interrupt (Electrocute is the backup interrupt, bosses are stun immune)
                    Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),
                    Spell.Cast("Electrocute",
                        ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts && !Core.Player.Target.BossOrGreater()),

                    //Consume Wrath carried from a previous channel before refreshing setup effects.
                    Spell.Cast("Demolish", ret => Core.Player.BuffCount("Wrath") >= 4),

                    //DoTs first, everything else in Madness scales off them.
                    Spell.DoT("Affliction", "Affliction"),
                    Spell.DoT("Creeping Terror", "Creeping Terror"),

                    //Rotation
                    //Death Field is a 5 m sphere placed at the target, castable only within 10 m.
                    Spell.CastOnGround("Death Field", ret => Core.Player.Target.Distance <= DeathFieldRange),   // applies Deathmark, spreads DoTs
                    Spell.Cast("Demolish", ret => Core.Player.BuffCount("Wrath") >= 4 || Core.Player.Level < 50),
                    Spell.Cast("Force Leech"),
                    Spell.Cast("Lightning Strike", ret => Core.Player.BuffCount("Wrath") >= 4),
                    Spell.Cast("Shock", ret => Core.Player.Level < 27),   // pre-Plague Master filler only

                    //Filler / Wrath builder
                    Spell.Cast("Force Lightning"),
                    Spell.Cast("Saber Strike", ret => Core.Player.ForcePercent <= 25)
                    );
            }
        }


        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.DoT("Affliction", "Affliction"),
                        Spell.DoT("Creeping Terror", "Creeping Terror"),
                        //Death Field is a 5 m sphere placed at the target, castable only within 10 m.
                        Spell.CastOnGround("Death Field", ret => Core.Player.Target.Distance <= DeathFieldRange && Targeting.ShouldTargetAoe(0.5f)),
                        Spell.Cast("Demolish", ret => Core.Player.BuffCount("Wrath") >= 4 || Core.Player.Level < 50),
                        //Force Storm is an 8 m sphere placed at the target, castable within 30 m.
                        Spell.CastOnGround("Force Storm", ret => Core.Player.Target.Distance <= 3.0f && Targeting.ShouldTargetAoe(0.8f)),
                        Spell.Cast("Force Lightning")
                        ));
            }
        }
    }
}
