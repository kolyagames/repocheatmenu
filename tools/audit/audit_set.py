"""GameReflect.Set(obj, "field", value): FieldInfo.SetValue does NOT convert int<->float etc. Check that the
value expression's evident type matches the field's declared type (by name, from the real decompile)."""
import io, os, re, sys
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'audit_reflect2.py'), encoding='utf-8').read()
exec(src.split("start_re = re.compile")[0])

MODDIR = r'C:\Users\Kolya\Desktop\REPO-чит\RepoCheatMenu'

def literal_type(expr):
    e = expr.strip()
    if re.fullmatch(r'-?\d+\.\d*f|-?\d+f|-?\.\d+f', e): return 'float'
    if re.fullmatch(r'-?\d+', e): return 'int'
    if re.fullmatch(r'-?\d+\.\d+', e): return 'double'
    if e in ('true', 'false'): return 'bool'
    if e.startswith('"'): return 'string'
    if re.fullmatch(r'Vector3\.\w+|new Vector3\(.*\)', e): return 'Vector3'
    m = re.match(r'(?:\(int\)|Mathf\.RoundToInt|Mathf\.FloorToInt|Mathf\.CeilToInt)', e)
    if m: return 'int'
    if re.match(r'\(float\)', e): return 'float'
    return None

out = []
for f in sorted(os.listdir(MODDIR)):
    if not f.endswith('.cs') or f in ('GameReflect.cs', 'ScriptGlobals.cs'):
        continue
    text = io.open(os.path.join(MODDIR, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'GameReflect\.Set\(\s*([^,]+),\s*"(\w+)",\s*', text):
        rest = text[m.end():m.end() + 300]
        depth = 0; val = ''
        for ch in rest:
            if ch == '(':
                depth += 1
            elif ch == ')':
                if depth == 0: break
                depth -= 1
            val += ch
        name = m.group(2)
        lt = literal_type(val)
        types = {norm(t) for t in fields.get(name, [])}
        line = text.count('\n', 0, m.start()) + 1
        if lt and types and lt not in types:
            out.append(f'{f}:{line} | Set "{name}" = {val.strip()} ({lt}) but field is {sorted(types)}')
        elif not lt:
            out.append(f'CHECK {f}:{line} | Set "{name}" = {val.strip()[:60]} -> field {sorted(types)}')
print('\n'.join(out))
