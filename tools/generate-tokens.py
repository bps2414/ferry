#!/usr/bin/env python3
"""Gera os tokens visuais da web e do Windows a partir de tools/tokens.json.

Uso: python3 tools/generate-tokens.py           # reescreve os blocos gerados
     python3 tools/generate-tokens.py --check   # falha se algum bloco estiver desatualizado (CI)
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOKENS = json.loads((ROOT / "tools" / "tokens.json").read_text(encoding="utf-8"))


def pascal(name: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in name.split("-"))


def css_block() -> str:
    colors = TOKENS["colors"]
    sizes = TOKENS["sizes"]
    lines = ["  " + " ".join(f"--{k}: {v};" for k, v in list(colors.items())[i:i + 6]) for i in range(0, len(colors), 6)]
    lines.append("  " + " ".join(f"--{k}: {v}px;" for k, v in sizes.items()))
    return "\n".join(lines)


def xaml_block() -> str:
    indent = " " * 8
    out = []
    for k, v in TOKENS["colors"].items():
        out.append(f'{indent}<Color x:Key="{pascal(k)}Color">{v}</Color>')
    for k in TOKENS["colors"]:
        out.append(f'{indent}<SolidColorBrush x:Key="{pascal(k)}" Color="{{StaticResource {pascal(k)}Color}}"/>')
    for k, v in TOKENS["sizes"].items():
        out.append(f'{indent}<sys:Double x:Key="{pascal(k)}Size">{v}</sys:Double>')
    return "\n".join(out)


TARGETS = [
    (ROOT / "web" / "ui" / "app.css", re.compile(r"(/\* tokens:start[^\n]*\*/\n)(.*?)(\n\s*/\* tokens:end \*/)", re.S), css_block),
    (ROOT / "app" / "App.xaml", re.compile(r"(<!-- tokens:start[^\n]*-->\n)(.*?)(\n\s*<!-- tokens:end -->)", re.S), xaml_block),
]


def main() -> int:
    check = "--check" in sys.argv[1:]
    stale = []
    for path, pattern, build in TARGETS:
        source = path.read_text(encoding="utf-8")
        match = pattern.search(source)
        if not match:
            print(f"{path.relative_to(ROOT)}: marcadores tokens:start/tokens:end ausentes", file=sys.stderr)
            return 1
        updated = source[:match.start(2)] + build() + source[match.end(2):]
        if updated == source:
            continue
        stale.append(path.relative_to(ROOT))
        if not check:
            path.write_text(updated, encoding="utf-8", newline="")
    if check and stale:
        print("Tokens desatualizados (rode python3 tools/generate-tokens.py): " + ", ".join(map(str, stale)), file=sys.stderr)
        return 1
    print("Tokens " + ("conferidos" if check else "gerados") + ".")
    return 0


if __name__ == "__main__":
    sys.exit(main())
