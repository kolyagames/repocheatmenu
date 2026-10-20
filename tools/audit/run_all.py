"""Run every static audit of the mod against the game decompile and print a short summary.

    python -I tools/audit/run_all.py

Each script prints its findings to stdout and a one-line summary to stderr. Needs decompiled/ and
decompiled-libs/ next to the project (see CLAUDE.md). What each one checks:
  audit_reflect2       GameReflect member names exist in the game; Get<T> type matches the field
  audit_reflect3       ...and exist on the target's own class chain (target type inferred)
  audit_access4        direct a.b.c member chains on game types use only PUBLIC members
  audit_harmony        Harmony patch targets exist, patch parameter names bind
  audit_overloads      no patch targets an overloaded method without argument types
  audit_invoke_overloads  GameReflect.Invoke never hits an overloaded method (AmbiguousMatchException)
  audit_rpc            .RPC("Name") exists as [PunRPC] with that argument count
  audit_rpc_types      RPC argument types match (Photon matches by exact types)
  audit_set            GameReflect.Set value type matches the field type
For direct-access bugs the stronger check is tools/check-real-refs.ps1 (compiles against the real DLLs).
"""
import os, subprocess, sys

here = os.path.dirname(os.path.abspath(__file__))
scripts = ['audit_reflect2', 'audit_reflect3', 'audit_access4', 'audit_harmony', 'audit_overloads',
           'audit_invoke_overloads', 'audit_rpc', 'audit_rpc_types', 'audit_set']
for name in scripts:
    r = subprocess.run([sys.executable, '-I', os.path.join(here, name + '.py')], cwd=here,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = [l for l in r.stdout.splitlines() if l.strip() and l.strip() != 'done' and not l.startswith('CHECK')]
    summary = (r.stderr.strip().splitlines() or ['(no summary)'])[-1]
    print(f'== {name}: {summary}')
    for line in out[:40]:
        print('   ' + line)
