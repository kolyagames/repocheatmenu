// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace RepoCheatMenu;

// Mouse and typing input for the CustomGui surfaces (F6 menu, HUD edit mode).
//
// Why this exists: all of that UI is drawn and hit-tested from TimeTickPatch, which is throttled to
// ~33 ticks a second (one per 30 ms) and fires from inside a Time.deltaTime getter. UnityEngine.Input's
// "went down this frame" / "went up this frame" / scroll / typed-characters values are valid for ONE
// frame only, so at 60-180 fps most clicks, wheel notches and keystrokes landed between two ticks and
// were never seen - the menu buttons simply did not react most of the time.
//
// Sample() is called on EVERY getter call (cheap: rate-limited to once a millisecond) and latches what
// it sees; the UI then reads the latches during its next tick and ClearLatches() drops them. Mouse
// buttons come from the OS (GetAsyncKeyState) like the hotkeys do; the wheel and typed text are read
// from UnityEngine.Input once per frame and accumulated.
internal static class UiInput
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static readonly int[] Vk = { 0x01, 0x02, 0x04 }; // left, right, middle
    private static readonly bool[] Prev = new bool[3];
    private static readonly bool[] HeldNow = new bool[3];
    private static readonly bool[] DownLatch = new bool[3];
    private static readonly bool[] UpLatch = new bool[3];

    private static UnityEngine.InputSystem.Keyboard _hookedKeyboard;
    private static long _lastSample;
    private static int _frame = -1;
    private static float _wheel;
    private static readonly StringBuilder TypedBuf = new();

    public static void Sample()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastSample < Stopwatch.Frequency / 1000) return;
        _lastSample = now;

        bool focused = Application.isFocused;
        for (int i = 0; i < 3; i++)
        {
            bool down = focused && (GetAsyncKeyState(Vk[i]) & 0x8000) != 0;
            if (down && !Prev[i]) DownLatch[i] = true;
            if (!down && Prev[i]) UpLatch[i] = true;
            Prev[i] = down;
            HeldNow[i] = down;
        }

        // Wheel and typed characters: Unity's value is the one for the CURRENT frame, so take it once per frame.
        // The game reads its input through the new Input System, so that is the first choice (text arrives
        // as events, the wheel as a per-frame delta of 120 per notch); the legacy Input class is only the
        // fallback when no new-system device exists.
        int frame = Time.frameCount;
        if (frame != _frame)
        {
            _frame = frame;
            bool textFromNewSystem = false;
            try
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null)
                {
                    if (!ReferenceEquals(_hookedKeyboard, keyboard))
                    {
                        if (_hookedKeyboard != null) _hookedKeyboard.onTextInput -= OnText;
                        keyboard.onTextInput += OnText;
                        _hookedKeyboard = keyboard;
                    }
                    textFromNewSystem = true;
                }

                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    _wheel += Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;
                }
                else
                {
                    _wheel += Input.mouseScrollDelta.y;
                }
            }
            catch { /* input system unavailable this frame */ }

            if (!textFromNewSystem)
            {
                try
                {
                    string typed = Input.inputString;
                    if (!string.IsNullOrEmpty(typed)) TypedBuf.Append(typed);
                }
                catch { /* legacy input unavailable this frame */ }
            }
        }
    }

    private static void OnText(char c) => TypedBuf.Append(c);

    // True while the player is typing into a text field of either menu: the new menu's own fields
    // (CustomGui focus) or a Unity InputField of the old one. Keybinds and the menu hotkey stay quiet then -
    // otherwise typing "f" fired whatever was bound to F, and a capital letter typed with Right Shift (the
    // menu key) closed the menu.
    // Also covers the game's own typing: the in-game chat (ChatManager.chatActive) and its TextMeshPro input
    // fields (lobby password, server search) - a bound letter typed into the chat used to toggle that feature.
    public static bool TypingInMenu()
    {
        if (CustomGui.AnyFieldFocused) return true;
        try
        {
            ChatManager chat = ChatManager.instance;
            if (chat != null && GameReflect.Get<bool>(chat, "chatActive")) return true;

            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            GameObject go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            UnityEngine.UI.InputField field = go.GetComponent<UnityEngine.UI.InputField>();
            if (field != null && field.isFocused) return true;
            TMPro.TMP_InputField tmp = go.GetComponent<TMPro.TMP_InputField>();
            return tmp != null && tmp.isFocused;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public static bool Down(int button) => button >= 0 && button < 3 && DownLatch[button];
    public static bool Up(int button) => button >= 0 && button < 3 && UpLatch[button];
    public static bool Held(int button) => button >= 0 && button < 3 && HeldNow[button];

    public static float Wheel()
    {
        float w = _wheel;
        _wheel = 0f;
        return w;
    }

    public static string Typed()
    {
        if (TypedBuf.Length == 0) return "";
        string s = TypedBuf.ToString();
        TypedBuf.Clear();
        return s;
    }

    // Called at the end of every UI tick: whatever it did not use is dropped, so a click made while the
    // menu was closed cannot fire on the next one.
    public static void ClearLatches()
    {
        for (int i = 0; i < 3; i++)
        {
            DownLatch[i] = false;
            UpLatch[i] = false;
        }
        _wheel = 0f;
        TypedBuf.Clear();
    }
}
