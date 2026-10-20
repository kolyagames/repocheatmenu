"""Type-aware direct-access audit.

Builds class -> {member: access} (+ base class) from decompiled/ (the REAL game assembly). Then for every
`<receiver>.<member>` in the mod where the receiver's type can be inferred (a local/parameter/field declared
with a game class type, `X.instance`, `__instance` in a patch for typeof(X), or a static `X.member`), it checks
the member's access along the class chain. Non-public -> runtime FieldAccessException/MethodAccessException.
"""
import io, os, re, sys, collections

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
GAME = os.path.join(ROOT, 'decompiled')

members = collections.defaultdict(dict)   # class -> member -> access
bases = {}
decl_re = re.compile(r'^(\t*)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|struct|enum)\s+(\w+)(?:<[^>]*>)?(?:\s*:\s*([\w\.<>]+))?')
mem_re = re.compile(r'^(\t+)(?:\[[^\]]*\]\s*)*(public|internal|private|protected)(?:\s+internal)?(?:\s+(?:static|readonly|new|volatile|unsafe|override|virtual|abstract|sealed|extern|async|const|event))*\s+[\w<>\[\],\.\? ]+?\s+@?(\w+)\s*(?:[=;({]|$)')
for f in os.listdir(GAME):
    if not f.endswith('.cs'):
        continue
    stack = []
    for line in io.open(os.path.join(GAME, f), encoding='utf-8', errors='replace'):
        line = line.rstrip('\r\n')
        m = decl_re.match(line)
        if m:
            d = len(m.group(1))
            stack = [s for s in stack if s[0] < d]
            stack.append((d, m.group(2)))
            if m.group(3):
                bases.setdefault(m.group(2), re.sub(r'<.*', '', m.group(3).split('.')[-1]))
            continue
        m = mem_re.match(line)
        if m and stack:
            indent = len(m.group(1))
            own = [s for s in stack if s[0] == indent - 1]
            if own:
                acc = members[own[-1][1]]
                name = m.group(3)
                # keep the most permissive access if overloaded
                if acc.get(name) != 'public':
                    acc[name] = m.group(2)

def lookup(cls, name):
    seen = 0
    while cls and seen < 15:
        if name in members.get(cls, {}):
            return cls, members[cls][name]
        cls = bases.get(cls)
        seen += 1
    return None, None

game_classes = set(members)
findings = []
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    lines = text.split('\n')
    # variable -> type, from declarations anywhere in the file (good enough: names are mostly unique per file)
    var_type = {}
    for m in re.finditer(r'(?:^|[\s(,])([A-Z]\w*)\??\s+(\w+)\s*(?=[=;,)]|\bin\b)', text, re.M):
        if m.group(1) in game_classes:
            var_type.setdefault(m.group(2), set()).add(m.group(1))
    patch_cls = None
    for i, line in enumerate(lines, 1):
        pm = re.search(r'HarmonyPatch\(typeof\((\w+)\)', line)
        if pm:
            patch_cls = pm.group(1)
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line.split('//')[0])
        code = re.sub(r'nameof\([^)]*\)', '', code)
        for m in re.finditer(r'(?<![\w.])((?:\w+\.)?\w+)\.(\w+)\b', code):
            recv, name = m.group(1), m.group(2)
            candidates = set()
            if recv in game_classes:                       # static access X.member
                candidates.add(recv)
            mi = re.fullmatch(r'(\w+)\.(?:instance|Instance)', recv)
            if mi and mi.group(1) in game_classes:
                candidates.add(mi.group(1))
            if recv == '__instance' and patch_cls in game_classes:
                candidates.add(patch_cls)
            if recv in var_type and '.' not in recv:
                candidates |= var_type[recv]
            for cls in candidates:
                owner, acc = lookup(cls, name)
                if owner and acc != 'public':
                    findings.append(f'{f}:{i} | {cls}.{name} is {acc} (declared on {owner}) | {line.strip()[:130]}')

for x in findings:
    print(x)
print(f'{len(findings)} findings', file=sys.stderr)
