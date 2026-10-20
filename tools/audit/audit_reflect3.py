"""Third pass: resolve the static type of the GameReflect target where possible and check that the member
is declared on that class or one of its base classes (per the decompile)."""
import io, os, re, sys, collections

ROOT = r'C:\Users\Kolya\Desktop\REPO-чит'
MOD = os.path.join(ROOT, 'RepoCheatMenu')
DECOMP = [os.path.join(ROOT, 'decompiled'), os.path.join(ROOT, 'decompiled-libs')]

cls_fields = collections.defaultdict(dict)   # class -> {field: type}
cls_methods = collections.defaultdict(set)
cls_base = {}
decl_re = re.compile(r'^(\t*)(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|struct)\s+(\w+)(?:<[^>]*>)?(?:\s*:\s*([\w\.<>]+))?')
member_re = re.compile(r'^\t+(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)(?:\s+(?:static|readonly|new|volatile|unsafe))*\s+([\w<>\[\],\.\? ]+?)\s+@?(\w+)\s*(=[^;]*)?;\s*$')
meth_re = re.compile(r'^\t+(?:public|internal|private|protected)(?:\s+(?:static|override|virtual|new|abstract|sealed|extern|unsafe|async))*\s+[\w<>\[\],\.\? ]+?\s+(\w+)\s*\(')
for base in DECOMP:
    for dirpath, _, files in os.walk(base):
        for f in files:
            if not f.endswith('.cs'):
                continue
            stack = []
            for line in io.open(os.path.join(dirpath, f), encoding='utf-8', errors='replace').read().split('\n'):
                line = line.rstrip('\r')
                m = decl_re.match(line)
                if m:
                    depth = len(m.group(1))
                    stack = [s for s in stack if s[0] < depth]
                    stack.append((depth, m.group(2)))
                    if m.group(3):
                        cls_base.setdefault(m.group(2), re.sub(r'<.*', '', m.group(3).split('.')[-1]))
                    continue
                if not stack:
                    continue
                # a member indented by N tabs belongs to the class declared at N-1 tabs (nested classes
                # end without a marker, so "last declared" would be wrong after them)
                indent = len(line) - len(line.lstrip('\t'))
                owners = [s for s in stack if s[0] == indent - 1]
                if not owners:
                    continue
                cur = owners[-1][1]
                m = member_re.match(line)
                if m and '(' not in line.split('=')[0]:
                    cls_fields[cur][m.group(2)] = m.group(1).replace(' ', '')
                    continue
                m = meth_re.match(line)
                if m:
                    cls_methods[cur].add(m.group(1))

def has_field(cls, name):
    seen = 0
    while cls and seen < 20:
        if name in cls_fields.get(cls, {}):
            return True
        cls = cls_base.get(cls)
        seen += 1
    return False

def has_method(cls, name):
    seen = 0
    while cls and seen < 20:
        if name in cls_methods.get(cls, set()):
            return True
        cls = cls_base.get(cls)
        seen += 1
    return False

def known(cls):
    return cls in cls_fields or cls in cls_methods

start_re = re.compile(r'GameReflect\.(Get|Set|Invoke)\b')
results = []
resolved = unresolved = 0
for f in sorted(os.listdir(MOD)):
    if not f.endswith('.cs') or f in ('GameReflect.cs', 'ScriptGlobals.cs'):
        continue
    text = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in start_re.finditer(text):
        line_no = text.count('\n', 0, m.start()) + 1
        if text.split('\n')[line_no - 1].strip().startswith('//'):
            continue
        rest = text[m.end():m.end() + 500]
        p = rest.find('(')
        depth = 0; i = p; comma = None
        while 0 <= i < len(rest):
            ch = rest[i]
            if ch == '(': depth += 1
            elif ch == ')':
                depth -= 1
                if depth == 0: break
            elif ch == ',' and depth == 1:
                comma = i; break
            i += 1
        if comma is None:
            continue
        target = rest[p + 1:comma].strip()
        mm = re.match(r'\s*"(\w+)"', rest[comma + 1:])
        if not mm:
            continue
        name = mm.group(1)
        cls = None
        # X.instance / X.Instance
        mi = re.fullmatch(r'(\w+)\.(?:instance|Instance)', target)
        if mi:
            cls = mi.group(1)
        # nested GameReflect.Get<T>(...)
        mg = re.match(r'GameReflect\.Get<(\w+)>', target)
        if mg:
            cls = mg.group(1)
        # PlayerTargets.Local / PlayerController.instance.X patterns
        if target in ('PlayerTargets.Local',):
            cls = 'PlayerAvatar'
        if cls is None and re.fullmatch(r'\w+', target):
            before = text[:m.start()]
            # nearest declaration of the identifier before this point
            pat = re.compile(r'(?:\(|,|\s|^)([A-Z]\w*)(?:<[^>]*>)?\??\s+' + re.escape(target) + r'\s*(?:=|;|\)|,|\bin\b|$)', re.M)
            decls = list(pat.finditer(before))
            if decls:
                cls = decls[-1].group(1)
            if target == '__instance' or (cls is None and target == 'this'):
                cls = None
        if cls is None or not known(cls):
            unresolved += 1
            if os.environ.get('SHOW_UNRESOLVED'):
                results.append(f'UNRESOLVED | {f}:{line_no} | {target} . {name} (guess {cls})')
            continue
        resolved += 1
        ok = has_method(cls, name) if m.group(1) == 'Invoke' else has_field(cls, name)
        if not ok:
            results.append(f'NOT-ON-CLASS | {f}:{line_no} | {cls}.{name} (target "{target}")')

print('\n'.join(results))
print(f'resolved {resolved}, unresolved {unresolved}, findings {len(results)}', file=sys.stderr)
