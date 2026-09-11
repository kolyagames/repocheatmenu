namespace RepoCheatMenu;

// Built-in debug fields the game ships with, confirmed guest-safe by reading their call sites
// in the decompiled source - none of them sit behind an IsMasterClient/IsMasterClientOrSingleplayer
// check, unlike most of the game's other debug fields (see CheatState.cs for the ones that
// were left out specifically because they turned out to be host-only).
internal static class RepoDebugHack
{
    public static void Apply()
    {
        if (ToolController.instance != null)
            ToolController.instance.DebugAlwaysInteract = CheatState.AlwaysInteract.Value;

        if (MoonUI.instance != null)
            GameReflect.Set(MoonUI.instance, "debugNoWait", CheatState.SkipMoonWait.Value);

        if (ResultScreenUI.instance != null)
            GameReflect.Set(ResultScreenUI.instance, "debugSkip", CheatState.SkipResultsScreen.Value);
    }
}
