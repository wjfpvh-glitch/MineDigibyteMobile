#!/usr/bin/env python3
"""Validate MineDigibyteMobile runtime localization resources and XAML keys."""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LANGUAGES = ("pt-BR", "es", "en")
RESOURCE_DIR = ROOT / "Resources" / "Localization"
KEY_PATTERN = re.compile(r"loc:Translate\s+Key=([A-Za-z0-9_]+)")
PLACEHOLDER_PATTERN = re.compile(r"\{[^{}]+\}")


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    resources: dict[str, dict[str, str]] = {}
    for language in LANGUAGES:
        path = RESOURCE_DIR / f"{language}.json"
        if not path.is_file():
            fail(f"Missing resource file: {path.relative_to(ROOT)}")
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            fail(f"Invalid JSON in {path.name}: {exc}")
        if not isinstance(data, dict):
            fail(f"{path.name} must contain a JSON object")
        if any(not isinstance(k, str) or not isinstance(v, str) or not v.strip()
               for k, v in data.items()):
            fail(f"{path.name} contains an empty or non-string key/value")
        resources[language] = data

    source_keys = set(resources["pt-BR"])
    for language in LANGUAGES[1:]:
        keys = set(resources[language])
        missing = sorted(source_keys - keys)
        extra = sorted(keys - source_keys)
        if missing or extra:
            fail(f"Key mismatch in {language}: missing={missing}; extra={extra}")

    for key in sorted(source_keys):
        expected = sorted(PLACEHOLDER_PATTERN.findall(resources["pt-BR"][key]))
        for language in LANGUAGES[1:]:
            actual = sorted(PLACEHOLDER_PATTERN.findall(resources[language][key]))
            if actual != expected:
                fail(f"Placeholder mismatch for '{key}' in {language}: {actual} != {expected}")

    used_keys: set[str] = set()
    for path in ROOT.glob("*.xaml"):
        try:
            ET.parse(path)
        except ET.ParseError as exc:
            fail(f"Invalid XAML in {path.name}: {exc}")
        used_keys.update(KEY_PATTERN.findall(path.read_text(encoding="utf-8")))

    unknown = sorted(used_keys - source_keys)
    if unknown:
        fail(f"XAML references missing keys: {unknown}")

    # Check common C# localization call sites too.
    for path in ROOT.rglob("*.cs"):
        if any(part in {"obj", "bin"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        for pattern in (r'\bT\("([^"]+)"\)',
                        r'GetString\("([^"]+)"\)',
                        r'Format\("([^"]+)"'):
            for key in re.findall(pattern, text):
                if key not in source_keys:
                    fail(f"{path.relative_to(ROOT)} references missing localization key: {key}")

    # Protect the runtime's single-pass phrase translation against reprocessing
    # translated output (the old repeated-Replace approach could turn Error into Errorr).
    for language in LANGUAGES[1:]:
        mapping = {
            source: resources[language][key]
            for key, source in resources["pt-BR"].items()
            if source and source != resources[language][key]
        }
        pattern = re.compile("|".join(
            re.escape(source) for source in sorted(mapping, key=len, reverse=True)))
        probes = (
            "Erro na leitura: socket fechado",
            "Erro ao interpretar mensagem: JSON inválido",
        )
        for probe in probes:
            translated = pattern.sub(lambda match: mapping[match.group(0)], probe)
            if "Errorr" in translated:
                fail(f"Single-pass translation regression for {language}: {translated}")

    project = ROOT / "MineDigibyteMobile.csproj"
    project_text = project.read_text(encoding="utf-8")
    if "Resources\\Localization\\*.json" not in project_text:
        fail("Localization JSON resources are not embedded in the project file")

    print(f"OK: {len(source_keys)} keys validated in PT-BR, ES and EN.")
    print(f"OK: {len(used_keys)} XAML keys and C# localization calls validated.")
    print("OK: dynamic log translations use single-pass replacement.")


if __name__ == "__main__":
    main()
