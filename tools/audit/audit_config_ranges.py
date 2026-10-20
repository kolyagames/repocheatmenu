"""config.Bind(section, key, default, new ConfigDescription(..., new AcceptableValueRange<T>(min, max))):
BepInEx throws at bind time if the default is outside the range - that happens inside CheatState.Init and
stops the whole mod from loading. Also flags AcceptableValueList defaults not in the list, and menu steppers
whose min/max fall outside the bound range (the stepper would then fight the clamp)."""
import io, os, re, sys

MOD = r'C:\Users\Kolya\Desktop\REPO-чит\RepoCheatMenu'
text = io.open(os.path.join(MOD, 'CheatState.cs'), encoding='utf-8-sig').read()

def num(s):
    return float(s.rstrip('fFdDmM'))

ranges = {}
bad = []
for m in re.finditer(r'(\w+)\s*=\s*config\.Bind\(\s*"[^"]*",\s*"(\w+)",\s*([^,]+?),', text):
    field, key, default = m.group(1), m.group(2), m.group(3).strip()
    tail = text[m.end():m.end() + 900]
    stop = tail.find(');\n')
    tail = tail[:stop if stop > 0 else len(tail)]
    r = re.search(r'AcceptableValueRange<(\w+)>\(\s*([-\d\.fF]+)\s*,\s*([-\d\.fF]+)\s*\)', tail)
    if r:
        lo, hi = num(r.group(2)), num(r.group(3))
        ranges[field] = (lo, hi)
        try:
            d = num(default)
            if not (lo <= d <= hi):
                bad.append(f'DEFAULT OUT OF RANGE: {field} default {default} not in [{r.group(2)}, {r.group(3)}]')
        except ValueError:
            pass
    l = re.search(r'AcceptableValueList<string>\(([^)]*)\)', tail)
    if l:
        opts = re.findall(r'"([^"]*)"', l.group(1))
        dv = default.strip('"')
        if default.startswith('"') and dv not in opts:
            bad.append(f'DEFAULT NOT IN LIST: {field} "{dv}" not in {opts}')

for f in ('CustomOverlayUI.cs', 'OverlayUI.cs'):
    ui = io.open(os.path.join(MOD, f), encoding='utf-8-sig').read()
    for m in re.finditer(r'(?:FloatStepper|IntStepper|AddFloatStepperRow|AddIntStepperRow)\([^,]+,\s*CheatState\.(\w+),\s*([-\d\.fF]+),\s*([-\d\.fF]+)', ui):
        field = m.group(1)
        if field in ranges:
            lo, hi = ranges[field]
            slo, shi = num(m.group(2)), num(m.group(3))
            if slo < lo or shi > hi:
                line = ui.count('\n', 0, m.start()) + 1
                bad.append(f'STEPPER WIDER THAN RANGE: {f}:{line} {field} stepper [{m.group(2)}, {m.group(3)}] vs bind [{lo}, {hi}]')

print('\n'.join(bad))
print(f'{len(ranges)} ranged settings, {len(bad)} findings', file=sys.stderr)
