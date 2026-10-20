"""Harmony audit: every [HarmonyPatch(typeof(X), "M")] class in the mod -> does X.M exist in the real game
decompile, and does every patch parameter bind (original parameter names, ___fields on X's chain,
__instance/__result/__state/__args/__originalMethod/__exception/__runOriginal)."""
import io, os, re, sys, collections

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
GAME = os.path.join(ROOT, 'decompiled')

methods = collections.defaultdict(list)    # (class, name) -> [param name lists]
fields = collections.defaultdict(set)
props = collections.defaultdict(set)
bases = {}
decl_re = re.compile(r'^(\t*)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|struct)\s+(\w+)(?:<[^>]*>)?(?:\s*:\s*([\w\.<>]+))?')
meth_re = re.compile(r'^(\t+)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)(?:\s+internal)?(?:\s+(?:static|override|virtual|new|abstract|sealed|extern|unsafe|async))*\s+[\w<>\[\],\.\? ]+?\s+(\w+)\s*\((.*)\)\s*$')
field_re = re.compile(r'^(\t+)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)(?:\s+(?:static|readonly|new|volatile|const))*\s+[\w<>\[\],\.\? ]+?\s+@?(\w+)\s*(=[^;]*)?;\s*$')
prop_re = re.compile(r'^(\t+)(?:public|internal|private|protected)(?:\s+(?:static|override|virtual|new))*\s+[\w<>\[\],\.\? ]+?\s+(\w+)\s*$')
for f in os.listdir(GAME):
    if not f.endswith('.cs'):
        continue
    stack = []
    lines = io.open(os.path.join(GAME, f), encoding='utf-8', errors='replace').read().split('\n')
    for idx, raw in enumerate(lines):
        line = raw.rstrip('\r')
        m = decl_re.match(line)
        if m:
            d = len(m.group(1))
            stack = [s for s in stack if s[0] < d]
            stack.append((d, m.group(2)))
            if m.group(3):
                bases.setdefault(m.group(2), re.sub(r'<.*', '', m.group(3).split('.')[-1]))
            continue
        if not stack:
            continue
        def owner(indent):
            o = [s for s in stack if s[0] == indent - 1]
            return o[-1][1] if o else None
        m = meth_re.match(line)
        if m:
            cls = owner(len(m.group(1)))
            if cls:
                params = []
                for p in re.split(r',(?![^<]*>)', m.group(3)):
                    p = re.sub(r'=.*', '', p).strip()
                    if p:
                        params.append(p.split()[-1].lstrip('@'))
                methods[(cls, m.group(2))].append(params)
            continue
        m = field_re.match(line)
        if m and '(' not in line.split('=')[0]:
            cls = owner(len(m.group(1)))
            if cls:
                fields[cls].add(m.group(2))
            continue
        m = prop_re.match(line)
        if m and idx + 1 < len(lines) and lines[idx + 1].strip() == '{':
            cls = owner(len(m.group(1)))
            if cls:
                props[cls].add(m.group(2))
                # property getter "get_X"
                methods[(cls, 'get_' + m.group(2))].append([])
                methods[(cls, m.group(2))].append([])

def chain(cls):
    out = []
    while cls and len(out) < 15:
        out.append(cls)
        cls = bases.get(cls)
    return out

def has_field(cls, name):
    return any(name in fields.get(c, ()) for c in chain(cls))

def find_methods(cls, name):
    res = []
    for c in chain(cls):
        res += methods.get((c, name), [])
    return res

special = {'__instance', '__result', '__state', '__args', '__originalMethod', '__exception', '__runOriginal'}
findings = []
patches = 0
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'\[HarmonyPatch\(typeof\((\w+)\),\s*"(\w+)"(?:,\s*([^\)]*))?\)\]\s*(?:\[[^\]]*\]\s*)*internal static class (\w+)\s*\{', text):
        cls, meth, extra, pcls = m.group(1), m.group(2), m.group(3), m.group(4)
        patches += 1
        # class body up to the next top-level "\n}"
        body_start = m.end()
        body_end = text.find('\n}', body_start)
        body = text[body_start:body_end]
        if cls not in bases and not any(k[0] == cls for k in methods):
            findings.append(f'{pcls}: type {cls} not in game decompile (library type? check by hand)')
            continue
        is_getter = extra and 'Getter' in extra
        cands = find_methods(cls, ('get_' + meth) if is_getter else meth)
        if not cands and not is_getter:
            findings.append(f'{pcls}: {cls}.{meth} NOT FOUND in decompile')
            continue
        orig_params = set(p for c in cands for p in c)
        for pm in re.finditer(r'private static \w+ (Prefix|Postfix|Finalizer|Transpiler)\s*\(([^)]*)\)', body):
            kind = pm.group(1)
            if kind == 'Transpiler':
                continue
            for p in [x.strip() for x in re.split(r',(?![^<]*>)', pm.group(2)) if x.strip()]:
                name = p.split()[-1]
                if name in special:
                    continue
                if name.startswith('___'):
                    if not has_field(cls, name[3:]):
                        findings.append(f'{pcls}.{kind}: field {name[3:]} not on {cls} chain')
                    continue
                if name.startswith('__') and name[2:].isdigit():
                    continue
                if name not in orig_params:
                    findings.append(f'{pcls}.{kind}: param "{name}" not in {cls}.{meth}{sorted(orig_params)}')
print('\n'.join(findings))
print(f'{patches} attribute patches checked, {len(findings)} findings', file=sys.stderr)
