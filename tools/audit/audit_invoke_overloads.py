import io, os, re, sys, collections
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'audit_harmony.py'), encoding='utf-8').read()
exec(src.split("special = {")[0])

by_name = collections.defaultdict(list)
for (cls, name), lst in methods.items():
    by_name[name].append((cls, len(lst)))

MODDIR = r'C:\Users\Kolya\Desktop\REPO-чит\RepoCheatMenu'
for f in sorted(os.listdir(MODDIR)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MODDIR, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'GameReflect\.Invoke\(\s*[^,]+,\s*"(\w+)"', text):
        name = m.group(1)
        multi = [(c, n) for c, n in by_name.get(name, []) if n > 1]
        if multi:
            line = text.count('\n', 0, m.start()) + 1
            print(f'{f}:{line} Invoke "{name}" - overloaded in {multi} (GetMethod throws AmbiguousMatchException)')
print('done')
