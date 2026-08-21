// Boosted active hotkey contract: 1.0.45.8
// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System.Windows.Forms;
using System.Windows.Input;
using BuddyCron;
using BuddyCron.Managers;
using DefaultCombat.Behaviors;
using Reborn.Utilities;

namespace DefaultCombat.Helpers
{
    /// <summary>Runtime rotation toggles (AoE, interrupts, charge, raid buffs, pause) bound to
    /// F4–F8/F12 hotkeys.</summary>
    public static class BoostedCombatHotkeys
    {
        private static readonly string[] s_hotkeyNames =
        {
            "Toggle RaidBuffs (F4)",
            "Toggle Interrupts (F5)",
            "Toggle Charge (F6)",
            "Toggle AOE (F7)",
            "Pause Rotation (F8)",
            "Set Tank (F12)"
        };

        private static bool s_initialized;

        /// <summary>Allow AoE abilities in the rotation.</summary>
        public static bool EnableAoe;
        /// <summary>Suspend the rotation entirely.</summary>
        public static bool PauseRotation;
        /// <summary>Allow interrupt abilities.</summary>
        public static bool EnableInterrupts;
        /// <summary>Allow gap-closer/charge abilities.</summary>
        public static bool EnableCharge;
        /// <summary>Keep raid-wide buffs applied.</summary>
        public static bool EnableRaidBuffs;

        /// <summary>True when AoE is enabled locally and allowed by the active bot context.</summary>
        public static bool AoeAllowed =>
            RoutineSettings.Instance.AreaDamage != RoutineActionMode.Disabled &&
            (RoutineSettings.Instance.AreaDamage == RoutineActionMode.Auto || EnableAoe) &&
            !RoutineManager.IsAnyDisallowed(CapabilityFlags.Aoe);

        /// <summary>True when interrupts are enabled locally and allowed by the active bot context.</summary>
        public static bool InterruptsEnabled =>
            RoutineSettings.Instance.Interrupts != RoutineActionMode.Disabled &&
            EnableInterrupts &&
            !RoutineManager.IsAnyDisallowed(CapabilityFlags.Interrupting);

        /// <summary>Compatibility gate used by existing rotations. Interrupt abilities now fire
        /// only for allow-listed casts; the shared handler also covers off-target casters.</summary>
        public static bool InterruptsAllowed =>
            InterruptsEnabled && Core.Player != null && Interrupts.IsPriorityCast(Core.Player.Target);

        /// <summary>True when gap closers are enabled locally and allowed by the active bot context.</summary>
        public static bool ChargeAllowed =>
            RoutineSettings.Instance.Movement != RoutineActionMode.Disabled &&
            EnableCharge &&
            !RoutineManager.IsAnyDisallowed(CapabilityFlags.GapCloser);

        /// <summary>True when raid cooldowns are enabled locally and offensive cooldowns are allowed.</summary>
        public static bool RaidBuffsAllowed =>
            RoutineSettings.Instance.RaidBuffs != RoutineActionMode.Disabled &&
            EnableRaidBuffs &&
            !RoutineManager.IsAnyDisallowed(CapabilityFlags.OffensiveCooldowns);

        /// <summary>Sets the default toggles and registers the shared hotkeys once.</summary>
        public static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }
            s_initialized = true;

            ApplySettingsDefaults();
            PauseRotation = false;

            //F9 and F10 are reservered for internal commands

            HotkeyManager.Register("Toggle RaidBuffs (F4)", Keys.F4, ModifierKeys.None, hk => ChangeRaidBuffs());
            Logger.Write("[Hot Key][F4] Toggle Raid Buffs");

            HotkeyManager.Register("Toggle Interrupts (F5)", Keys.F5, ModifierKeys.None, hk => ChangeInterrupts());
            Logger.Write("[Hot Key][F5] Toggle Interrupts");

            HotkeyManager.Register("Toggle Charge (F6)", Keys.F6, ModifierKeys.None, hk => ChangeCharge());
            Logger.Write("[Hot Key][F6] Toggle Charge");

            HotkeyManager.Register("Toggle AOE (F7)", Keys.F7, ModifierKeys.None, hk => ChangeAoe());
            Logger.Write("[Hot Key][F7] Toggle AOE");
            Logger.Write("[AOE] Mode={0}, Enabled={1}, Allowed={2}",
                RoutineSettings.Instance.AreaDamage, EnableAoe, AoeAllowed);

            HotkeyManager.Register("Pause Rotation (F8)", Keys.F8, ModifierKeys.None, hk => ChangePause());
            Logger.Write("[Hot Key][F8] Pause Rotation");

            Logger.Write("[Hot Key][F9] Pause/Resume Bot");

            Logger.Write("[Hot Key][F10] Start/Stop Bot");

            HotkeyManager.Register("Set Tank (F12)", Keys.F12, ModifierKeys.None, hk => Targeting.SetTank());
            Logger.Write("[Hot Key][F12] Set Tank");
        }

        /// <summary>Applies the active profile to temporary hotkey toggles.</summary>
        public static void ApplySettingsDefaults()
        {
            EnableAoe = RoutineSettings.Instance.AreaDamage == RoutineActionMode.Auto;
            EnableInterrupts = RoutineSettings.Instance.Interrupts == RoutineActionMode.Auto;
            EnableCharge = RoutineSettings.Instance.Movement == RoutineActionMode.Auto;
            EnableRaidBuffs = RoutineSettings.Instance.RaidBuffs == RoutineActionMode.Auto;
        }

        /// <summary>Unregisters shared hotkeys and resets transient toggle state on routine unload.</summary>
        public static void Shutdown()
        {
            if (!s_initialized)
                return;

            foreach (var hotkeyName in s_hotkeyNames)
                HotkeyManager.Unregister(hotkeyName);

            s_initialized = false;
            EnableAoe = false;
            PauseRotation = false;
            EnableInterrupts = false;
            EnableCharge = false;
            EnableRaidBuffs = false;
        }

        private static void ChangeAoe()
        {
            if (RoutineSettings.Instance.AreaDamage == RoutineActionMode.Disabled)
            {
                Logger.Write("AOE is Disabled in Routine Settings");
                return;
            }

            if (RoutineSettings.Instance.AreaDamage == RoutineActionMode.Auto)
            {
                Logger.Write("AOE is Automatic in Routine Settings");
                return;
            }

            if (EnableAoe)
            {
                Logger.Write("AOE Disabled");
                EnableAoe = false;
            }
            else
            {
                Logger.Write("AOE Enabled");
                EnableAoe = true;
            }
        }

        private static void ChangePause()
        {
            if (PauseRotation)
            {
                Logger.Write("Rotation Resumed");
                PauseRotation = false;
            }
            else
            {
                Logger.Write("Rotation Paused");
                PauseRotation = true;
            }
        }

        private static void ChangeCharge()
        {
            if (RoutineSettings.Instance.Movement == RoutineActionMode.Disabled)
            {
                Logger.Write("Charge is Disabled in Routine Settings");
                return;
            }

            if (EnableCharge)
            {
                Logger.Write("Charge Disabled");
                EnableCharge = false;
            }
            else
            {
                Logger.Write("Charge Enabled");
                EnableCharge = true;
            }
        }

        private static void ChangeInterrupts()
        {
            if (RoutineSettings.Instance.Interrupts == RoutineActionMode.Disabled)
            {
                Logger.Write("Interrupts are Disabled in Routine Settings");
                return;
            }

            if (EnableInterrupts)
            {
                Logger.Write("Interrupts Disabled");
                EnableInterrupts = false;
            }
            else
            {
                Logger.Write("Interrupts Enabled");
                EnableInterrupts = true;
            }
        }

        private static void ChangeRaidBuffs()
        {
            if (RoutineSettings.Instance.RaidBuffs == RoutineActionMode.Disabled)
            {
                Logger.Write("Raid Buffs are Disabled in Routine Settings");
                return;
            }

            if (EnableRaidBuffs)
            {
                Logger.Write("Raid Buffs Disabled");
                EnableRaidBuffs = false;
            }
            else
            {
                Logger.Write("Raid Buffs Enabled");
                EnableRaidBuffs = true;
            }
        }
    }
}
