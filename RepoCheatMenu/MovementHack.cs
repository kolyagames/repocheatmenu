// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Reflection;

namespace RepoCheatMenu;

// Direct reflection on PlayerController movement fields. Field names below come from a
// single community reference (not independently cross-confirmed like the StatsManager
// dictionary approach), so this is best-effort: if a field doesn't exist on the current
// game version, GetField returns null and we silently skip it instead of throwing.
internal static class MovementHack
{
    private static FieldInfo _moveSpeedField;
    private static FieldInfo _sprintSpeedField;
    private static FieldInfo _crouchSpeedField;
    private static FieldInfo _jumpExtraField;
    private static bool _resolved;

    private static float? _baseMoveSpeed;
    private static float? _baseSprintSpeed;
    private static float? _baseCrouchSpeed;
    private static float? _baseJumpForce;

    public static void ResetBaseline()
    {
        _baseMoveSpeed = null;
        _baseSprintSpeed = null;
        _baseCrouchSpeed = null;
        _baseJumpForce = null;
    }

    public static void Apply(PlayerController controller)
    {
        if (controller == null) return;

        if (!_resolved)
        {
            System.Type type = controller.GetType();
            _moveSpeedField = type.GetField("MoveSpeed", BindingFlags.Public | BindingFlags.Instance);
            _sprintSpeedField = type.GetField("SprintSpeed", BindingFlags.Public | BindingFlags.Instance);
            _crouchSpeedField = type.GetField("CrouchSpeed", BindingFlags.Public | BindingFlags.Instance);
            _jumpExtraField = type.GetField("JumpExtra", BindingFlags.NonPublic | BindingFlags.Instance);
            _resolved = true;
        }

        // Strafe applies in every direction equally because there's nothing direction-specific
        // to gate it on - confirmed in the decompiled PlayerController.FixedUpdate, InputDirection
        // (built straight from raw WASD/stick axes) is multiplied by MoveSpeed/SprintSpeed/
        // CrouchSpeed the same way no matter which way it's pointing.
        float strafeMult = CheatState.Strafe.Value ? CheatState.StrafeMultiplier.Value : 1f;
        if (CheatState.Strafe.Value)
        {
            // MoveMult() is the game's own public hook for temporary slow effects (hazards,
            // enemies) - confirmed in the decompiled source, it just sets a multiplier and a
            // countdown timer that PlayerController.FixedUpdate applies to InputDirection.
            // Resetting it to a no-op every tick is the actual thing that can make any
            // direction feel slower than another, since the slow itself is never direction-specific.
            controller.MoveMult(1f, 0f);
        }

        if (_moveSpeedField != null)
        {
            _baseMoveSpeed ??= (float)_moveSpeedField.GetValue(controller);
            if (CheatState.SpeedMultiplier.Value != 1f || strafeMult != 1f)
                _moveSpeedField.SetValue(controller, _baseMoveSpeed.Value * CheatState.SpeedMultiplier.Value * strafeMult);
        }

        if (_sprintSpeedField != null)
        {
            _baseSprintSpeed ??= (float)_sprintSpeedField.GetValue(controller);
            if (CheatState.SprintSpeedMultiplier.Value != 1f || strafeMult != 1f)
                _sprintSpeedField.SetValue(controller, _baseSprintSpeed.Value * CheatState.SprintSpeedMultiplier.Value * strafeMult);
        }

        if (_crouchSpeedField != null)
        {
            _baseCrouchSpeed ??= (float)_crouchSpeedField.GetValue(controller);
            if (CheatState.CrouchSpeedMultiplier.Value != 1f || strafeMult != 1f)
                _crouchSpeedField.SetValue(controller, _baseCrouchSpeed.Value * CheatState.CrouchSpeedMultiplier.Value * strafeMult);
        }

        if (CheatState.InfiniteJumps.Value && _jumpExtraField != null)
        {
            _jumpExtraField.SetValue(controller, 99);
        }

        _baseJumpForce ??= controller.JumpForce;
        if (CheatState.JumpMultiplier.Value != 1f)
            controller.JumpForce = _baseJumpForce.Value * CheatState.JumpMultiplier.Value;
    }
}
