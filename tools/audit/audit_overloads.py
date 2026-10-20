import io, os, re, sys
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'audit_harmony.py'), encoding='utf-8').read()
exec(src.split("special = {")[0])

MODDIR = r'C:\Users\Kolya\Desktop\REPO-чит\RepoCheatMenu'
for f in sorted(os.listdir(MODDIR)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MODDIR, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'\[HarmonyPatch\(typeof\((\w+)\),\s*"(\w+)"(,[^\]]*)?\)\]', text):
        cls, meth, extra = m.group(1), m.group(2), m.group(3) or ''
        own = methods.get((cls, meth), [])
        if len(own) > 1 and 'new[]' not in extra and 'typeof' not in extra:
            print(f'{f}: {cls}.{meth} has {len(own)} overloads {own} - attribute gives no argument types')
print('done')
