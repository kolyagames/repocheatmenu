"""RPC argument TYPE audit. Photon matches an incoming RPC to a method by name AND exact parameter types:
an int sent where the method takes float (or the reverse) is "not found" on the receiver.
Infers each argument's type from: literals, casts, typed declarations in the file, well-known members
(.ViewID int, .position Vector3, .rotation Quaternion, .Value of CheatState entries), and reports mismatches."""
import io, os, re, sys, collections
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'audit_rpc.py'), encoding='utf-8').read()
exec(src.split("findings = []")[0])

CHEAT = io.open(os.path.join(MOD, 'CheatState.cs'), encoding='utf-8-sig').read()
entry_type = {m.group(2): m.group(1) for m in re.finditer(r'public static ConfigEntry<(\w+)> (\w+);', CHEAT)}

def norm(t):
    t = t.strip().replace('UnityEngine.', '')
    return {'Int32': 'int', 'Single': 'float', 'Boolean': 'bool', 'String': 'string'}.get(t, t)

def infer(expr, text, pos):
    e = expr.strip()
    if re.fullmatch(r'-?\d+(\.\d*)?f|-?\.\d+f', e): return 'float'
    if re.fullmatch(r'-?\d+', e): return 'int'
    if re.fullmatch(r'-?\d+\.\d+', e): return 'double'
    if e in ('true', 'false'): return 'bool'
    if e.startswith('"') or e.startswith('$"'): return 'string'
    m = re.match(r'\((int|float|bool|string|byte|short)\)', e)
    if m: return m.group(1)
    if re.match(r'Mathf\.(RoundToInt|FloorToInt|CeilToInt)\b', e): return 'int'
    if re.match(r'Mathf\.(Clamp01|Lerp|Max|Min|Clamp)\b', e) and 'f' in e: return 'float'
    if e.endswith('.ViewID') or e.endswith('.ActorNumber'): return 'int'
    if e.endswith('.position') or e.endswith('.forward') or e.startswith('Vector3.') or e.startswith('new Vector3('): return 'Vector3'
    if e.endswith('.rotation') or e.startswith('Quaternion.'): return 'Quaternion'
    m = re.fullmatch(r'CheatState\.(\w+)\.Value', e)
    if m and m.group(1) in entry_type: return entry_type[m.group(1)]
    if re.fullmatch(r'\w+', e):
        decls = list(re.finditer(r'(?:^|[\s(,])(int|float|bool|string|Vector3|Quaternion|byte|short|double)\s+' + re.escape(e) + r'\s*(?=[=;,)])', text[:pos], re.M))
        if decls: return decls[-1].group(1)
        # const / field declared anywhere in the file
        m = re.search(r'(?:const|static|readonly)\s+(int|float|bool|string|double)\s+' + re.escape(e) + r'\b', text)
        if m: return m.group(1)
    return None

out = []
checked = 0
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'\.RPC\(\s*"(\w+)"\s*,', text):
        name = m.group(1)
        if name not in rpcs:
            continue
        args = split_args(text[m.end():m.end() + 600])[1:]
        line = text.count('\n', 0, m.start()) + 1
        overloads = [[norm(t) for t in types if 'PhotonMessageInfo' not in t] for _, types in rpcs[name]]
        overloads = [o for o in overloads if len(o) == len(args)]
        if not overloads:
            continue
        inferred = [infer(a, text, m.start()) for a in args]
        ok = False
        for o in overloads:
            if all(i is None or i == t or (t == 'object') for i, t in zip(inferred, o)):
                ok = True
                break
        checked += 1
        if not ok:
            out.append(f'{f}:{line} | {name}({", ".join(str(i) for i in inferred)}) vs game {overloads}')
print('\n'.join(out))
print(f'{checked} RPC sends type-checked, {len(out)} mismatches', file=sys.stderr)
