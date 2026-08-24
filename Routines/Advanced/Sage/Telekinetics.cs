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
    ///     7.x Sage Telekinetics (ranged burst DPS) rotation, built around the Weaken Mind
    ///     auto-crit setup for Turbulence and the Tidal Force / Force Gust procs.
    /// </summary>
    public class Telekinetics : RotationBase
    {
        private const double MindCrushMinimumTtd = 8;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Telekinetics;

        public override string Name => "Sage Telekinetics";

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

                    //Start Mental Alacrity before applying the long-lived DoT. Hold Force Potency
                    //for Telekinetic Gust's Force Gust window instead of wasting charges on setup.
                    Spell.Buff("Force Empowerment", ret => CombatHotkeys.EnableRaidBuffs),
                    Spell.Cast("Mental Alacrity",
                        ret => Core.Player.InCombat && Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (!Core.Player.Target.HasMyDebuff("Weaken Mind") || Core.Player.HasBuff("Force Gust"))),
                    Spell.Cast("Force Potency",
                        ret => Core.Player.HasBuff("Force Gust") || !AbilityManager.HasAbility("Telekinetic Gust")),

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

                    //Rotation (7.x priority: Weaken Mind > Turbulence > Telekinetic Gust > Mind Crush >
                    //          Telekinetic Wave/Power of the Force on Tidal Force > Project > Telekinetic Burst)
                    Spell.Cast("Mind Snap", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Weaken Mind must be up: it makes Turbulence an auto-crit
                    Spell.DoT("Weaken Mind", "Weaken Mind"),
                    Spell.Cast("Turbulence"),

                    //Telekinetic Gust applies Stormwatch and grants Force Gust (faster Mind Crush)
                    Spell.Cast("Telekinetic Gust"),
                    Spell.Cast("Mind Crush",
                        ret => Core.Player.HasBuff("Force Gust") &&
                               TimeToDie.WillLiveFor(Core.Player.Target, MindCrushMinimumTtd)),

                    //Tidal Force makes the next Telekinetic Wave (or Power of the Force) instant + cheap.
                    //Power of the Force is an ability-tree choice (lvl 23) -- skipped if not taken.
                    Spell.Cast("Power of the Force", ret => Core.Player.HasBuff("Tidal Force")),
                    Spell.Cast("Telekinetic Wave", ret => Core.Player.HasBuff("Tidal Force")),

                    //Project is the follow-up to Mind Crush. Mind Crush's DoT is named "Crushed (Force)".
                    //Before Mind Crush is trained Project is just a filler.
                    Spell.Cast("Project", ret => Core.Player.Target.HasMyDebuff("Crushed (Force)") || Core.Player.Level < 30),

                    //Mind Crush on cooldown even without the Force Gust proc
                    Spell.Cast("Mind Crush",
                        ret => TimeToDie.WillLiveFor(Core.Player.Target, MindCrushMinimumTtd)),

                    //Telekinetic Blitz is a movement fallback unless a tactical-specific AoE policy owns it.
                    Spell.Cast("Telekinetic Blitz", ret => Core.Player.IsMoving),

                    //Fillers -- Telekinetic Burst is the discipline filler, Disturbance covers low levels,
                    //Saber Strike is the free attack so the rotation can never stall
                    Spell.Cast("Telekinetic Burst"),
                    Spell.Cast("Disturbance"),
                    Spell.Cast("Saber Strike", ret => Core.Player.Target.Distance <= Distance.Melee)
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

                        //Keep Weaken Mind up for the Turbulence auto-crit
                        Spell.DoT("Weaken Mind", "Weaken Mind"),

                        Spell.Cast("Power of the Force", ret => Core.Player.HasBuff("Tidal Force")),
                        Spell.Cast("Telekinetic Wave", ret => Core.Player.HasBuff("Tidal Force")),
                        Spell.Cast("Telekinetic Wave"),
                        Spell.Cast("Turbulence"),
                        Spell.CastOnGround("Forcequake")
                        ));
            }
        }
    }
}
