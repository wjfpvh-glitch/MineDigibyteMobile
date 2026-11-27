#!/usr/bin/env python3
"""Regression cases for the C# device-language mapping used at first launch."""


def map_device_language(tag: str) -> str:
    primary = tag.replace("_", "-").split("-", 1)[0].strip().lower()
    return {"pt": "pt-BR", "es": "es", "en": "en"}.get(primary, "pt-BR")


def main() -> None:
    cases = {
        "en": "en",
        "en-US": "en",
        "en-GB": "en",
        "es": "es",
        "es-ES": "es",
        "es-MX": "es",
        "pt": "pt-BR",
        "pt-BR": "pt-BR",
        "pt-PT": "pt-BR",
        "fr-FR": "pt-BR",
        "de-DE": "pt-BR",
    }
    for source, expected in cases.items():
        actual = map_device_language(source)
        assert actual == expected, f"{source}: expected {expected}, got {actual}"
    print(f"OK: {len(cases)} culture mapping cases")


if __name__ == "__main__":
    main()
