// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

namespace DefaultCombat.Helpers
{
    /// <summary>Shared rotation constants and enums.</summary>
    public class Global
    {
        /// <summary>Group role categories used for tank/heal targeting.</summary>
        public enum PartyRole
        {
            MeleeTank,
            RangedTank,
            MeleeDPS,
            RangedDPS,
            Healer
        }

        /// <summary>Energy floor rotations try to stay above.</summary>
        public const int EnergyMinimum = 65;
        /// <summary>Ammo/cell floor rotations try to stay above.</summary>
        public const int CellMinimum = 8;
    }

    /// <summary>Range thresholds (game distance units) used by rotations and targeting. Ability
    /// ranges are compared against <see cref="BuddyCron.Objects.HeroCharacter.EdgeDistance"/>, the
    /// distance to the target's hit edge, which is the metric the game validates casts with.</summary>
    public class Distance
    {
        /// <summary>Melee ability range, from the target's hit edge.</summary>
        public const float Melee = 0.4f;
        /// <summary>Radius to look for extra targets around a melee AoE.</summary>
        public const float MeleeAoE = 0.8f;
        /// <summary>Standard ranged ability range.</summary>
        public const float Ranged = 2.8f;
        /// <summary>Radius an AoE heal is expected to cover.</summary>
        public const float HealAoe = 1.2f;
        /// <summary>Extended ranged range, for abilities that outrange <see cref="Ranged"/>.</summary>
        public const float RangedExt = 3.4f;
    }


    /// <summary>Health-percentage thresholds used by rotations.</summary>
    public class Health
    {
        /// <summary>Treat at or above this percentage as full health.</summary>
        public const int Max = 95;
        /// <summary>Shield/absorb cooldowns become worthwhile below this percentage.</summary>
        public const int Shield = 90;
        /// <summary>Light damage — cheap heals only.</summary>
        public const int High = 85;
        /// <summary>Moderate damage — use the main heal.</summary>
        public const int Mid = 50;
        /// <summary>Heavy damage — use strong/instant heals.</summary>
        public const int Low = 35;
        /// <summary>Emergency: use defensive cooldowns and medpacs.</summary>
        public const int Critical = 15;
    }
}
