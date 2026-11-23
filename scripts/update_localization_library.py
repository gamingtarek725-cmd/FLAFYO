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
UA = "FLAFYO-LibraryBot/1.1 (+https://github.com/gamingtarek-cmd/FLAFYO)"
SOURCES = [
    ("Ta3reebat", "https://ta3reebat.com/"),
    ("Muarrab", "https://muarrab.com/en/patches/home/"),
    ("Ta3reb", "https://ta3reb.com/"),
]
PLATFORM_TOKENS = ("PC", "PS5", "PS4", "PS3", "Xbox", "Switch", "Android")
TITLE_OVERRIDES = {
    "007-first-light": "007 First Light",
    "a-plague-tale-innocence": "A Plague Tale: Innocence",
    "grand-theft-auto-v": "Grand Theft Auto V",
    "red-dead-redemption-2": "Red Dead Redemption 2",
    "resident-evil-4-ultimate-hd-edition": "Resident Evil 4 Ultimate HD Edition",
    "the-last-of-us-2": "The Last of Us Part II",
    "the-witcher-3": "The Witcher 3: Wild Hunt",
    "it-takes-two": "It Takes Two",
    "lies-of-p": "Lies of P",
    "hollow-knight-silksong": "Hollow Knight: Silksong",
}

class LinkParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links = []
        self.active = None
    def handle_starttag(self, tag, attrs):
        if tag.lower() == "a":
            self.active = {"href": dict(attrs).get("href", ""), "text": "", "cover": ""}
        elif tag.lower() == "img" and self.active is not None and not self.active["cover"]:
            image_attrs = dict(attrs)
            self.active["cover"] = (image_attrs.get("data-src") or image_attrs.get("data-lazy-src")
                                    or image_attrs.get("data-original") or image_attrs.get("src") or "")
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

class PageImageParser(HTMLParser):
    """Read social preview artwork from a game's own localization page."""
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.candidates = []
    def handle_starttag(self, tag, attrs):
        values = {str(k).lower(): (v or "").strip() for k, v in attrs}
        if tag.lower() == "meta":
            key = (values.get("property") or values.get("name") or values.get("itemprop") or "").lower()
            image = values.get("content", "")
            if image and key in {"og:image", "og:image:url", "twitter:image", "twitter:image:src", "image"}:
                self.candidates.append(image)
        elif tag.lower() == "link" and "image_src" in values.get("rel", "").lower().split():
            if values.get("href"):
                self.candidates.append(values["href"])

def safe_image_url(page_url: str, candidates: list[str]) -> str:
    for candidate in candidates:
        absolute = urljoin(page_url, html.unescape(candidate.strip()))
        parsed = urlparse(absolute)
        if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password:
            continue
        path = parsed.path.lower()
        # WPF's built-in image decoder supports these common formats; skip SVG/WebP.
        if path and not re.search(r"\.(?:jpe?g|png|gif|bmp|tiff?)$", path):
            continue
        if any(token in path for token in ("/logo", "favicon", "avatar", "icon")):
            continue
        return absolute
    return ""

def discover_cover(entry: dict) -> tuple[str, str]:
    try:
        parser = PageImageParser()
        parser.feed(fetch(entry["sourceUrl"]))
        cover = safe_image_url(entry["sourceUrl"], parser.candidates)
        return entry["sourceUrl"], cover
    except Exception as exc:
        print(f"WARN: artwork unavailable for {entry['title']}: {exc}")
        return entry["sourceUrl"], ""

def title_from_slug(path: str, fallback: str) -> str:
    slug = path.rstrip("/").split("/")[-1].lower()
    if slug in TITLE_OVERRIDES:
        return TITLE_OVERRIDES[slug]
    # URL slugs are more stable than link text polluted by badges, view counts and platform labels.
    words = re.sub(r"[-_]+", " ", slug).strip()
    words = re.sub(r"(?i)^ta3reeb( of)? ", "", words)
    if not words or words in {"home", "patches", "game", "translations"}:
        return fallback[:180]
    return words.title()[:180]

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
            raw_title, href = link["text"], link["href"]
            if not raw_title or not href or href.startswith(("#", "javascript:", "mailto:")):
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
            is_game_page = (
                source_name == "Ta3reebat" and "/translations/" in path
                or source_name == "Muarrab" and "/patches/" in path
                or source_name == "Ta3reb" and "/game/" in path
            )
            if not is_game_page or absolute in seen:
                continue
            seen.add(absolute)
            arrow_versions = re.search(r"(\d+(?:\.\d+){1,3})\s*→\s*(\d+(?:\.\d+){1,3})", raw_title)
            prefixed_version = re.search(r"(?i)\bv\s*(\d+(?:\.\d+){1,3})", raw_title)
            translation_version = arrow_versions.group(2) if arrow_versions else (prefixed_version.group(1) if prefixed_version else "")
            is_new = bool(re.match(r"^\s*جديد\b", raw_title)) or bool(re.search(r"جديد\s+الإصدار\s+الأول", raw_title, re.I))
            is_update = bool(arrow_versions) or bool(re.search(r"محدّث|محدث|تحديث", raw_title, re.I))
            update_type = "new" if is_new else ("update" if is_update else "listing")
            title = title_from_slug(path, raw_title)
            platforms = [token for token in PLATFORM_TOKENS
                         if re.search(rf"(?<![A-Za-z0-9]){re.escape(token)}(?![A-Za-z0-9])", raw_title, re.I)]
            entries.append({
                "title": title,
                "sourceUrl": absolute,
                "sourceName": source_name,
                "coverUrl": urljoin(listing_url, link.get("cover", "")) if link.get("cover") else "",
                "platform": ", ".join(platforms),
                "gameVersion": "",
                "translationVersion": translation_version,
                "updateType": update_type,
                "summary": raw_title[:500],
                "publishedAt": ""
            })
    entries.sort(key=lambda item: (item["sourceName"], item["title"].casefold()))
    previous = {}
    if OUTPUT.exists():
        try:
            previous = json.loads(OUTPUT.read_text(encoding="utf-8"))
        except Exception:
            previous = {}

    # Preserve manually verified covers, then enrich missing artwork from each game's
    # own page metadata. Fetch concurrently so the scheduled catalog refresh stays bounded.
    previous_covers = {
        item.get("sourceUrl"): item.get("coverUrl", "")
        for item in previous.get("entries", [])
        if item.get("sourceUrl") and item.get("coverUrl")
    }
    for entry in entries:
        if not entry["coverUrl"] and previous_covers.get(entry["sourceUrl"]):
            entry["coverUrl"] = previous_covers[entry["sourceUrl"]]

    from concurrent.futures import ThreadPoolExecutor, as_completed
    missing_artwork = [entry for entry in entries if not entry["coverUrl"]]
    with ThreadPoolExecutor(max_workers=8) as executor:
        futures = [executor.submit(discover_cover, entry) for entry in missing_artwork[:400]]
        for future in as_completed(futures):
            source_url, cover_url = future.result()
            if cover_url:
                match = next((item for item in entries if item["sourceUrl"] == source_url), None)
                if match is not None:
                    match["coverUrl"] = cover_url

    entries.sort(key=lambda item: (item["sourceName"], item["title"].casefold()))
    with_cover = sum(bool(item["coverUrl"]) for item in entries)
    print(f"Artwork found for {with_cover}/{len(entries)} game pages.")
    unchanged = previous.get("entries") == entries and previous.get("sourceCount") == len(SOURCES)
    generated_at = previous.get("generatedAt") if unchanged and previous.get("generatedAt") else dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")
    catalog = {
        "schemaVersion": 2,
        "generatedAt": generated_at,
        "sourceCount": len(SOURCES),
        "entries": entries[:1200]
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Indexed {len(catalog['entries'])} public game pages from {len(SOURCES)} source sites.")

if __name__ == "__main__":
    main()
