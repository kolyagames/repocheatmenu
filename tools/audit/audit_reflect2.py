"""Second pass: for EVERY GameReflect.Get/Set/Invoke occurrence take the first string literal after it
(the member name) and the generic argument, and check existence + type. Also prints how many were checked."""
import io, os, re, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
DECOMP = [os.path.join(ROOT, 'decompiled'), os.path.join(ROOT, 'decompiled-libs')]

fields = collections.defaultdict(set)
props = collections.defaultdict(set)
methods = set()
member_re = re.compile(r'^\t{1,3}(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)(?:\s+(?:static|readonly|new|volatile|unsafe))*\s+([\w<>\[\],\.\? ]+?)\s+@?(\w+)\s*(=[^;]*)?;\s*$')
prop_re = re.compile(r'^\t{1,3}(?:public|internal|private|protected)(?:\s+(?:static|override|virtual|new|abstract|sealed))*\s+([\w<>\[\],\.\? ]+?)\s+(\w+)\s*$')
meth_re = re.compile(r'^\t{1,3}(?:public|internal|private|protected)(?:\s+(?:static|override|virtual|new|abstract|sealed|extern|unsafe|async))*\s+[\w<>\[\],\.\? ]+?\s+(\w+)\s*\(')
for base in DECOMP:
    for dirpath, _, files in os.walk(base):
        for f in files:
            if not f.endswith('.cs'):
                continue
            lines = io.open(os.path.join(dirpath, f), encoding='utf-8', errors='replace').read().split('\n')
            for idx, line in enumerate(lines):
                line = line.rstrip('\r')
                m = member_re.match(line)
                if m and '(' not in line.split('=')[0]:
                    fields[m.group(2)].add(m.group(1).replace(' ', ''))
                    continue
                m = prop_re.match(line)
                if m and idx + 1 < len(lines) and lines[idx + 1].strip().startswith('{'):
                    props[m.group(2)].add(m.group(1))
                    continue
                m = meth_re.match(line)
                if m:
                    methods.add(m.group(1))

def norm(t):
    t = t.replace(' ', '')
    t = re.sub(r'\b\w+\.', '', t)
    return {'Int32': 'int', 'Single': 'float', 'Boolean': 'bool', 'String': 'string'}.get(t, t)

start_re = re.compile(r'GameReflect\.(Get|Set|Invoke)\b')
checked = 0
out = []
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs') or f in ('GameReflect.cs', 'ScriptGlobals.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in start_re.finditer(text):
        line_no = text.count('\n', 0, m.start()) + 1
        line = text.split('\n')[line_no - 1].strip()
        if line.startswith('//'):
            continue
        rest = text[m.end():m.end() + 400]
        T = None
        if rest.startswith('<'):
            depth = 0
            for j, ch in enumerate(rest):
                if ch == '<': depth += 1
                elif ch == '>':
                    depth -= 1
                    if depth == 0:
                        T = rest[1:j]
                        break
        # first string literal after the opening paren, skipping nested GameReflect calls' own literal:
        # find the matching comma at depth 1
        p = rest.find('(')
        if p < 0:
            continue
        depth = 0
        name = None
        i = p
        while i < len(rest):
            ch = rest[i]
            if ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
                if depth == 0:
                    break
            elif ch == ',' and depth == 1:
                mm = re.match(r'\s*"(\w+)"', rest[i + 1:])
                if mm:
                    name = mm.group(1)
                break
            i += 1
        if not name:
            continue
        checked += 1
        kind = m.group(1)
        where = f'{f}:{line_no}'
        if kind == 'Invoke':
            if name not in methods:
                out.append(('MISSING-METHOD', where, name))
            continue
        if name not in fields:
            out.append(('PROPONLY' if name in props else 'MISSING', where, name))
            continue
        if kind == 'Get' and T:
            nt = norm(T)
            types = {norm(t) for t in fields[name]}
            if nt not in types and nt not in ('object',):
                out.append(('TYPE', where, name, f'Get<{T}> declared {sorted(types)}'))

for o in out:
    print(' | '.join(o))
print(f'checked {checked} calls, {len(out)} findings', file=sys.stderr)
