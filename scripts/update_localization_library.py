#!/usr/bin/env python3
"""Index public Arabic game-localization pages without downloading patch files."""
from __future__ import annotations
import datetime as dt
import html
import json
import re
import urllib.request
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urlparse

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "data" / "localization-library.json"
UA = "FLAFYO-LibraryBot/1.0 (+https://github.com/gamingtarek725-cmd/FLAFYO)"
SOURCES = [
    ("Ta3reebat", "https://ta3reebat.com/"),
    ("Muarrab", "https://muarrab.com/en/patches/home/"),
    ("Ta3reb", "https://ta3reb.com/"),
]
VERSION_RE = re.compile(r"(?i)\bv\s*\d+(?:\.\d+){1,3}|\b\d+\.\d+\.\d+(?:\.\d+)?\b")

class LinkParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links = []
        self.active = None
    def handle_starttag(self, tag, attrs):
        if tag.lower() == "a":
            self.active = {"href": dict(attrs).get("href", ""), "text": ""}
    def handle_data(self, data):
        if self.active is not None:
            self.active["text"] += data
    def handle_endtag(self, tag):
        if tag.lower() == "a" and self.active is not None:
            self.active["text"] = re.sub(r"\s+", " ", html.unescape(self.active["text"])).strip()
            self.links.append(self.active)
            self.active = None

def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(request, timeout=25) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        return response.read(8_000_000).decode(charset, errors="replace")

def main():
    entries, seen = [], set()
    for source_name, listing_url in SOURCES:
        try:
            parser = LinkParser()
            parser.feed(fetch(listing_url))
        except Exception as exc:
            print(f"WARN: {source_name} could not be checked: {exc}")
            continue
        base_host = urlparse(listing_url).hostname
        for link in parser.links:
            title, href = link["text"], link["href"]
            if not title or not href or href.startswith(("#", "javascript:", "mailto:")):
                continue
            absolute = urljoin(listing_url, href).split("#", 1)[0]
            parsed = urlparse(absolute)
            if parsed.scheme != "https" or not parsed.hostname:
                continue
            if parsed.hostname != base_host and not parsed.hostname.endswith("." + base_host):
                continue
            path = parsed.path.rstrip("/")
            if path in ("", "/", "/en", "/en/patches/home"):
                continue
            if source_name == "Muarrab" and ("/tags/" in path or "/teams/" in path):
                continue
            relevant = (
                source_name == "Ta3reebat" and (
                    "/translations/" in path or "تعريب" in title or "→" in title or "جديد" in title
                )
                or source_name == "Muarrab" and "/patches/" in path
                or source_name == "Ta3reb" and ("تعريب" in title or "/game/" in path)
            )
            if not relevant or absolute in seen:
                continue
            seen.add(absolute)
            versions = VERSION_RE.findall(title)
            is_new = bool(re.search(r"جديد|new release|first release|إصدار أول", title, re.I))
            is_update = bool(re.search(r"تحديث|updated|update|→", title, re.I))
            update_type = "new" if is_new else ("update" if is_update else "listing")
            entries.append({
                "title": title[:180],
                "sourceUrl": absolute,
                "sourceName": source_name,
                "platform": "",
                "gameVersion": "",
                "translationVersion": " / ".join(versions[-2:]) if versions else "",
                "updateType": update_type,
                "summary": title[:500],
                "publishedAt": ""
            })
    entries.sort(key=lambda item: (item["sourceName"], item["title"].casefold()))
    catalog = {
        "schemaVersion": 1,
        "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "sourceCount": len(SOURCES),
        "entries": entries[:1200]
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Indexed {len(catalog['entries'])} public source pages.")

if __name__ == "__main__":
    main()
