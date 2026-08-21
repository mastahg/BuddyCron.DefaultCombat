using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron.Objects;

namespace DefaultCombat.Routines
{
    internal static class LightningPvPEffects
    {
        private sealed class EffectRule
        {
            internal readonly string[] Names;
            internal readonly HashSet<ulong> AbilitySpecIds;

            internal EffectRule(string[] names, params ulong[] abilitySpecIds)
            {
                Names = names;
                AbilitySpecIds = new HashSet<ulong>(abilitySpecIds);
            }

            internal bool Matches(HeroEffect effect)
            {
                if (effect == null)
                    return false;

                if (effect.AbilitySpecId != 0 && AbilitySpecIds.Contains(effect.AbilitySpecId))
                    return true;

                var name = effect.Name ?? string.Empty;
                return Names.Any(candidate =>
                    string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            }

            internal void Learn(HeroEffect effect)
            {
                if (effect == null || effect.AbilitySpecId == 0)
                    return;

                var name = effect.Name ?? string.Empty;
                if (Names.Any(candidate =>
                        string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
                {
                    AbilitySpecIds.Add(effect.AbilitySpecId);
                }
            }
        }

        private static readonly EffectRule DamageImmunityEffects = new EffectRule(
            new[]
            {
                "Force Barrier",
                "Undying Rage",
                "Guarded by the Force",
                "Emergency Power",
                "Force Shroud",
                "Resilience",
                "Covered Escape"
            });

        private static readonly EffectRule DamageReturnEffects = new EffectRule(
            new[]
            {
                "Saber Reflect",
                "Responsive Safeguards",
                "Echoing Deterrence"
            });

        private static readonly EffectRule BurstHoldEffects = new EffectRule(
            new[]
            {
                "Enraged Defense",
                "Focused Defense",
                "Kolto Overload",
                "Adrenaline Rush",
                "Evasion",
                "Deflection"
            });

        private static readonly EffectRule ResolveImmunityEffects = new EffectRule(
            new[]
            {
                "Unstoppable",
                "Hydraulic Overrides",
                "Hold the Line",
                "Force Shroud",
                "Resilience",
                "Unshakable"
            });

        private static readonly string[] HardControlEffects =
        {
            "Stunned",
            "Electrocute",
            "Force Stun",
            "Debilitate",
            "Dirty Kick",
            "Electro Dart",
            "Cryo Grenade",
            "Carbonize",
            "Carbonized",
            "Force Choke",
            "Force Stasis",
            "Knocked Down"
        };

        private static readonly string[] MezControlEffects =
        {
            "Whirlwind",
            "Force Lift",
            "Mind Trap",
            "Mind Maze",
            "Sleep Dart",
            "Tranquilizer",
            "Flash Bang",
            "Flash Grenade",
            "Intimidating Roar",
            "Awe",
            "Concussion Missile",
            "Concussion Round"
        };

        private static readonly string[] ObjectiveCastFragments =
        {
            "Capture",
            "Capturing",
            "Turret",
            "Pylon",
            "Plant",
            "Bomb",
            "Arm",
            "Disarm",
            "Door",
            "Console",
            "Slice",
            "Orb",
            "Download",
            "Upload",
            "Override"
        };

        private static readonly string[] PriorityCastFragments =
        {
            "Heal",
            "Kolto",
            "Medical",
            "Medicine",
            "Deliverance",
            "Benevolence",
            "Dark Infusion",
            "Dark Heal",
            "Revive",
            "Resuscitate"
        };

        private static readonly string[] DangerousCastFragments =
        {
            "Ambush",
            "Aimed Shot",
            "Heatseeker",
            "Demolition Round",
            "Thundering Blast",
            "Turbulence",
            "Furious Strike",
            "Concentrated Slice"
        };

        internal static bool HasDamageImmunity(HeroCharacter unit)
        {
            return HasEffect(unit, DamageImmunityEffects);
        }

        internal static bool HasDamageReturn(HeroCharacter unit)
        {
            return HasEffect(unit, DamageReturnEffects);
        }

        internal static bool HasHealingDefensive(HeroCharacter unit)
        {
            return HasEffect(unit, BurstHoldEffects);
        }

        internal static bool HasBurstHold(HeroCharacter unit)
        {
            return HasDamageImmunity(unit) ||
                   HasDamageReturn(unit) ||
                   HasHealingDefensive(unit);
        }

        internal static bool HasResolveImmunity(HeroCharacter unit)
        {
            return HasEffect(unit, ResolveImmunityEffects);
        }

        internal static string ActiveDefensiveName(HeroCharacter unit)
        {
            var name = ActiveEffectName(unit, DamageImmunityEffects);
            if (!string.IsNullOrEmpty(name))
                return name;

            name = ActiveEffectName(unit, DamageReturnEffects);
            if (!string.IsNullOrEmpty(name))
                return name;

            return ActiveEffectName(unit, BurstHoldEffects);
        }

        internal static bool IsPriorityCast(HeroCharacter unit)
        {
            if (unit == null || !unit.IsCasting || unit.CastingAbility == null ||
                unit.CastTimeRemaining <= 0.1f)
            {
                return false;
            }

            var name = unit.CastingAbility.Name ?? string.Empty;
            return IsObjectiveCast(unit) ||
                   PriorityCastFragments.Any(fragment =>
                       name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   DangerousCastFragments.Any(fragment =>
                       name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsObjectiveCast(HeroCharacter unit)
        {
            if (unit == null || !unit.IsCasting || unit.CastingAbility == null ||
                unit.CastTimeTotal < 3.5f || unit.CastTimeRemaining <= 0.1f)
            {
                return false;
            }

            var name = unit.CastingAbility.Name ?? string.Empty;
            return ObjectiveCastFragments.Any(fragment =>
                name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool HasControlEffect(HeroCharacter unit)
        {
            return unit != null && unit.Debuffs.Any(effect => ControlResolveRate(effect) > 0);
        }

        internal static double ControlResolveRate(HeroEffect effect)
        {
            if (effect == null)
                return 0;

            var name = effect.Name ?? string.Empty;
            if (HardControlEffects.Any(candidate =>
                    string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                return 200;
            }

            if (MezControlEffects.Any(candidate =>
                    string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                return 100;
            }

            return 0;
        }

        internal static void Learn(HeroEffect effect)
        {
            DamageImmunityEffects.Learn(effect);
            DamageReturnEffects.Learn(effect);
            BurstHoldEffects.Learn(effect);
            ResolveImmunityEffects.Learn(effect);
        }

        private static bool HasEffect(HeroCharacter unit, EffectRule rule)
        {
            return unit != null && unit.Buffs.Concat(unit.Debuffs).Any(rule.Matches);
        }

        private static string ActiveEffectName(HeroCharacter unit, EffectRule rule)
        {
            if (unit == null)
                return string.Empty;

            var effect = unit.Buffs.Concat(unit.Debuffs).FirstOrDefault(rule.Matches);
            return effect != null ? effect.Name ?? string.Empty : string.Empty;
        }
    }
}
