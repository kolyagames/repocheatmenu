"""Settings that do nothing: a CheatState entry that is shown in a menu but whose value is never read by any
feature code (only by the menus, the panic sweep, keybind registration or CheatState itself)."""
import io, os, re, sys

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
UI_FILES = {'CheatState.cs', 'CustomOverlayUI.cs', 'CustomOverlayUI.Dropdown.cs', 'OverlayUI.cs', 'PanicHack.cs',
            'KeybindManager.cs', 'PresetsHack.cs', 'ScriptGlobals.cs'}

cheat = io.open(os.path.join(MOD, 'CheatState.cs'), encoding='utf-8-sig').read()
names = re.findall(r'public static (?:ConfigEntry<\w+>|HudWidgetConfig) (\w+);', cheat)

texts = {f: io.open(os.path.join(MOD, f), encoding='utf-8-sig').read() for f in os.listdir(MOD) if f.endswith('.cs')}
dead, ui_only = [], []
for n in names:
    pat = re.compile(r'CheatState\.' + n + r'\b')
    feature_refs = [f for f, t in texts.items() if f not in UI_FILES and pat.search(t)]
    # CheatState itself may use a setting (e.g. inside a SettingChanged handler)
    self_refs = len(re.findall(r'\b' + n + r'\.(?:Value|SettingChanged)', cheat))
    in_menu = any(pat.search(texts[f]) for f in ('CustomOverlayUI.cs', 'OverlayUI.cs') if f in texts)
    if not feature_refs and self_refs == 0:
        (ui_only if in_menu else dead).append(n)

print('SHOWN IN A MENU BUT NEVER READ BY FEATURE CODE:')
for n in ui_only:
    print('  ' + n)
print('NEVER READ AND NOT IN ANY MENU:')
for n in dead:
    print('  ' + n)
print(f'{len(ui_only)} ui-only, {len(dead)} unused', file=sys.stderr)
