"""RPC audit: every .RPC("Name", target, args...) and GameReflect.Invoke(obj, "NameRPC", args...) in the mod
-> a [PunRPC] method of that name exists in the game decompile, and the argument count matches one of its
overloads (PhotonMessageInfo excluded for .RPC; for Invoke all declared parameters are required - Mono's
MethodInfo.Invoke does not fill optional defaults)."""
import io, os, re, sys, collections

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
GAME = os.path.join(ROOT, 'decompiled')

rpcs = collections.defaultdict(list)   # name -> [(class, [param types])]
for f in os.listdir(GAME):
    if not f.endswith('.cs'):
        continue
    lines = io.open(os.path.join(GAME, f), encoding='utf-8', errors='replace').read().split('\n')
    for i, l in enumerate(lines):
        if '[PunRPC]' in l and i + 1 < len(lines):
            m = re.search(r'\s(\w+)\s*\((.*)\)', lines[i + 1])
            if m:
                ps = [p.strip() for p in re.split(r',(?![^<]*>)', m.group(2)) if p.strip()]
                types = [re.sub(r'\s*=.*', '', p).rsplit(' ', 1)[0] for p in ps]
                rpcs[m.group(1)].append((f[:-3], types))

def split_args(s):
    out, depth, cur, q = [], 0, '', False
    for ch in s:
        if ch == '"':
            q = not q
        if not q:
            if ch in '([{<':
                depth += 1 if ch != '<' else 0
            elif ch in ')]}':
                if depth == 0:
                    break
                depth -= 1
            elif ch == ',' and depth == 0:
                out.append(cur.strip()); cur = ''; continue
        cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out

findings = []
n = 0
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'\.RPC\(\s*"(\w+)"\s*,', text):
        line = text.count('\n', 0, m.start()) + 1
        if text.split('\n')[line - 1].strip().startswith('//'):
            continue
        name = m.group(1)
        args = split_args(text[m.end():m.end() + 600])
        nargs = len(args) - 1  # first is the target
        n += 1
        if name not in rpcs:
            findings.append(f'{f}:{line} | RPC "{name}" has no [PunRPC] in the game')
            continue
        counts = set()
        for cls, types in rpcs[name]:
            t = [x for x in types if 'PhotonMessageInfo' not in x]
            counts.add(len(t))
        if nargs not in counts:
            findings.append(f'{f}:{line} | RPC "{name}" sent with {nargs} args, game expects {sorted(counts)} ({[c for c, _ in rpcs[name]]})')
    for m in re.finditer(r'GameReflect\.Invoke\(\s*([^,]+),\s*"(\w+)"', text):
        line = text.count('\n', 0, m.start()) + 1
        if text.split('\n')[line - 1].strip().startswith('//'):
            continue
        name = m.group(2)
        if name not in rpcs:
            continue  # not an RPC - plain method, checked elsewhere
        args = split_args(text[m.end():m.end() + 600])
        args = [a for a in args if a]
        n += 1
        counts = {len(types) for _, types in rpcs[name]}
        if len(args) not in counts:
            findings.append(f'{f}:{line} | Invoke "{name}" with {len(args)} args, declared {sorted(counts)} params (Mono needs ALL, incl. PhotonMessageInfo)')
print('\n'.join(findings))
print(f'{n} calls checked, {len(findings)} findings', file=sys.stderr)
