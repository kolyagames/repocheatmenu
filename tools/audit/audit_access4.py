"""Direct-access audit v4: typed member chains. a.b.c.d -> type of a (declaration / static class / X.instance),
then each hop's declared field/property type from the real game decompile. Every hop whose member is
non-public is reported. Also counts how many hops were checked, for coverage."""
import io, os, re, sys, collections

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
GAME = os.path.join(ROOT, 'decompiled')

acc_of = collections.defaultdict(dict)    # class -> member -> access
type_of = collections.defaultdict(dict)   # class -> member -> declared type (fields/properties)
bases = {}
decl_re = re.compile(r'^(\t*)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|struct|enum)\s+(\w+)(?:<[^>]*>)?(?:\s*:\s*([\w\.<>]+))?')
mem_re = re.compile(r'^(\t+)(?:\[[^\]]*\]\s*)*(public|internal|private|protected)(?:\s+internal)?((?:\s+(?:static|readonly|new|volatile|unsafe|override|virtual|abstract|sealed|extern|async|const|event))*)\s+([\w<>\[\],\.\? ]+?)\s+@?(\w+)\s*([=;({]|$)')
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
            own = [s for s in stack if s[0] == len(m.group(1)) - 1]
            if not own:
                continue
            cls, name, access, typ, term = own[-1][1], m.group(5), m.group(2), m.group(4).strip(), m.group(6)
            if acc_of[cls].get(name) != 'public':
                acc_of[cls][name] = access
            if term != '(':
                type_of[cls][name] = re.sub(r'\?$', '', typ.split('.')[-1])

def lookup(cls, name, table):
    seen = 0
    while cls and seen < 15:
        if name in table.get(cls, {}):
            return cls, table[cls][name]
        cls = bases.get(cls)
        seen += 1
    return None, None

game = set(acc_of)
checked = 0
findings = []
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    lines = text.split('\n')
    offs = []
    p = 0
    for l in lines:
        offs.append(p); p += len(l) + 1
    decls = [(m.start(), m.group(2), m.group(1)) for m in
             re.finditer(r'(?:^|[\s(,])([A-Z]\w*)\??\s+(\w+)\s*(?=[=;,)]|\bin\b)', text, re.M)]
    for i, line in enumerate(lines, 1):
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line.split('//')[0])
        code = re.sub(r'nameof\([^)]*\)', '', code)
        for m in re.finditer(r'(?<![\w.])(\w+(?:\.\w+)+)', code):
            parts = m.group(1).split('.')
            head = parts[0]
            cls = None
            k = 1
            if head in game:
                cls = head
            elif head == 'this' or head == 'base':
                continue
            else:
                prev = [d for d in decls if d[0] < offs[i - 1] + m.start() and d[1] == head]
                if prev:
                    cls = prev[-1][2]
            if cls not in game:
                continue
            for name in parts[k:]:
                if name in ('instance', 'Instance') and cls in game:
                    owner, acc = lookup(cls, name, acc_of)
                    if owner and acc != 'public':
                        findings.append(f'{f}:{i} | {cls}.{name} is {acc} | {line.strip()[:120]}')
                    continue
                owner, acc = lookup(cls, name, acc_of)
                if owner is None:
                    break
                checked += 1
                if acc != 'public':
                    findings.append(f'{f}:{i} | {cls}.{name} is {acc} (on {owner}) | {line.strip()[:120]}')
                _, t = lookup(cls, name, type_of)
                if not t or t not in game:
                    break
                cls = t

for x in findings:
    print(x)
print(f'checked {checked} member hops, {len(findings)} findings', file=sys.stderr)
