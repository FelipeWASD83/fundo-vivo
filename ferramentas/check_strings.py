"""Checks the pt/en string tables against the source.

- every Loc.T("key") / {loc:T key} used in src/FundoVivo exists in Core/Strings/*.cs
- no duplicate keys, no empty texts, same {n} placeholders in pt and en
- (info) keys defined but never used
Run: python ferramentas/check_strings.py   (exit code 1 when something is wrong)
"""
import re, sys, pathlib

root = pathlib.Path(__file__).resolve().parent.parent / "src" / "FundoVivo"
BS = chr(92)
lit = '"((?:[^"' + BS + BS + ']|' + BS + BS + '.)*)"'
entry = re.compile(r'\(\s*' + lit + r'\s*,\s*' + lit + r'\s*,\s*' + lit + r'\s*\)')

table, problems = {}, []
for f in sorted((root / "Core" / "Strings").glob("*.cs")):
    for m in entry.finditer(f.read_text(encoding="utf-8-sig")):
        k, pt, en = m.groups()
        if k in table:
            problems.append(f"duplicate key {k} ({f.name})")
        table[k] = (pt, en)
        if not pt.strip() or not en.strip():
            problems.append(f"empty text: {k}")
        ph = lambda s: sorted(set(re.findall(r'\{(\d+)', s)))
        if ph(pt) != ph(en):
            problems.append(f"placeholders differ: {k}")

used = {}
for f in root.rglob("*"):
    if f.suffix not in (".cs", ".xaml") or "obj" in f.parts or "bin" in f.parts or "Strings" in f.parts or f.name == "Loc.cs":
        continue
    text = f.read_text(encoding="utf-8-sig")
    for m in re.finditer(r'Loc\.(?:T|All)\(\s*"([^"]+)"', text):
        used.setdefault(m.group(1), f.name)
    for m in re.finditer(r'\{loc:T\s+([A-Za-z0-9_.]+)\s*\}', text):
        used.setdefault(m.group(1), f.name)
    for m in re.finditer(r'Loc\.(?:T|All)\(\s*[^")\s]', text):
        problems.append(f"{f.name}: dynamic key (use literal keys): {text[m.start():m.start()+50]!r}")

for k, f in sorted(used.items()):
    if k != "app_name" and k not in table:
        problems.append(f"missing key {k} (used in {f})")
unused = sorted(set(table) - set(used))
print(f"{len(table)} keys, {len(used)} used, {len(unused)} unused")
if unused:
    print("unused:", ", ".join(unused[:60]))
for p in problems:
    print("PROBLEM:", p)
sys.exit(1 if problems else 0)
