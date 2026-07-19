"""Merge the Simple Icons brand set into the bundled icon registry.

Downloads the full `simple-icons` collection from Iconify's icon-sets (single
JSON with every icon body), extracts each single `<path d="...">`, and merges
them into `src/Netdocs.Core/Markdown/Emoji/data/icons.json.gz` under keys
`simple-<slug>` with the same `{ "p": <path>, "v": <viewBox> }` shape the
existing material/octicons/fontawesome entries use.
"""
import gzip
import io
import json
import re
import sys
import urllib.request
from pathlib import Path

ICONS_GZ = Path(__file__).resolve().parent.parent / "src/Netdocs.Core/Markdown/Emoji/data/icons.json.gz"
SOURCE = "https://raw.githubusercontent.com/iconify/icon-sets/master/json/simple-icons.json"

D_ATTR = re.compile(r'\bd="([^"]+)"')


def load_existing() -> dict:
    with gzip.open(ICONS_GZ, "rt", encoding="utf-8") as fh:
        return json.load(fh)


def fetch_simple() -> dict:
    req = urllib.request.Request(SOURCE, headers={"User-Agent": "netdocs-icon-merge"})
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.load(resp)


def extract_path(body: str):
    m = D_ATTR.search(body)
    return m.group(1) if m else None


def main() -> int:
    existing = load_existing()
    before = len(existing)
    coll = fetch_simple()

    width = coll.get("width", 24)
    height = coll.get("height", 24)

    icons = coll.get("icons", {})
    aliases = coll.get("aliases", {})

    added = 0
    skipped = 0
    resolved_bodies = {}

    for name, meta in icons.items():
        body = meta.get("body", "")
        path = extract_path(body)
        if not path:
            skipped += 1
            continue
        w = meta.get("width", width)
        h = meta.get("height", height)
        vb = f"0 0 {w} {h}"
        resolved_bodies[name] = {"p": path, "v": vb}
        existing[f"simple-{name}"] = resolved_bodies[name]
        added += 1

    for alias, meta in aliases.items():
        parent = meta.get("parent")
        if parent and parent in resolved_bodies:
            existing[f"simple-{alias}"] = resolved_bodies[parent]
            added += 1

    payload = json.dumps(existing, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode="wb", compresslevel=9, mtime=0) as gz:
        gz.write(payload)
    ICONS_GZ.write_bytes(buf.getvalue())

    print(f"existing icons before : {before}")
    print(f"simple icons added    : {added} (skipped {skipped} non-single-path)")
    print(f"total icons after     : {len(existing)}")
    print(f"has simple-splunk     : {'simple-splunk' in existing}")
    print(f"gz size               : {ICONS_GZ.stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
