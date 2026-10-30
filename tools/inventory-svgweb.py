"""Inventory the hosted SVGWeb originals without retaining their SVG payloads."""

import concurrent.futures
import datetime
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET


BASE = "https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/"
MAXIMUM_BYTES = 8 * 1024 * 1024
SVG = "http://www.w3.org/2000/svg"


class DirectoryLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            self.links.extend(value for name, value in attrs if name == "href" and value is not None)


def download(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Lokad.Svg sample inventory"})
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read(MAXIMUM_BYTES + 1)
        if len(body) > MAXIMUM_BYTES:
            raise ValueError("The research fetch exceeds the configured per-file byte bound.")
        return body, response.status, response.headers.get("Content-Type"), response.headers.get("Last-Modified")


def inspect(name):
    url = BASE + urllib.parse.quote(name)
    row = {"name": name, "url": url, "renderValidation": "pending"}
    try:
        body, status, content_type, modified = download(url)
    except (OSError, ValueError, urllib.error.HTTPError) as error:
        return row | {"retrieval": "failed", "error": str(error)}
    return inspect_body(name, body, status, content_type, modified)


def inspect_body(name, body, status, content_type, modified):
    url = BASE + urllib.parse.quote(name)
    row = {"name": name, "url": url, "renderValidation": "pending"}
    row.update(retrieval="fetched", bytes=len(body), sourceSha256=hashlib.sha256(body).hexdigest(),
               httpStatus=status, contentType=content_type, lastModified=modified,
               doctype=b"<!doctype" in body.lower())
    try:
        root = ET.fromstring(body)
    except ET.ParseError as error:
        return row | {"xmlInspection": "parse-error", "xmlError": str(error)}
    tags, attributes, fonts, css_fonts, dependencies = {}, {}, set(), set(), set()
    dependency_uses = []
    embedded = local = handlers = 0

    def inspect_reference(reference, tag, attribute):
        nonlocal embedded, local
        reference = reference.strip(" \t\r\n\"'")
        if reference.startswith("data:"):
            embedded += 1
        elif reference.startswith("#"):
            local += 1
        elif reference:
            resolved = urllib.parse.urljoin(url, reference)
            dependencies.add(resolved)
            use = {"element": tag, "attribute": attribute, "url": resolved}
            if use not in dependency_uses:
                dependency_uses.append(use)

    def inspect_css(css):
        for family in re.findall(r"(?:^|[;{])\s*font-family\s*:\s*([^;}]+)", css, re.IGNORECASE):
            css_fonts.add(family.strip())

    for element in root.iter():
        tag = element.tag.removeprefix("{" + SVG + "}")
        tags[tag] = tags.get(tag, 0) + 1
        for key, value in element.attrib.items():
            attribute = key.rsplit("}", 1)[-1]
            attributes[attribute] = attributes.get(attribute, 0) + 1
            handlers += attribute.startswith("on")
            if attribute == "font-family":
                fonts.add(value)
            if attribute == "style":
                inspect_css(value)
            references = [value] if attribute in ["href", "src"] else []
            references += re.findall(r"url\(\s*([^)]*?)\s*\)", value)
            for reference in references:
                inspect_reference(reference, tag, attribute)
        if tag == "style" and element.text:
            inspect_css(element.text)
            for reference in re.findall(r"url\(\s*([^)]*?)\s*\)", element.text):
                inspect_reference(reference, tag, "css-url")
    return row | {"xmlInspection": "parsed", "root": root.tag,
                  "dimensions": {key: root.attrib[key] for key in ["width", "height", "viewBox", "preserveAspectRatio"] if key in root.attrib},
                  "elements": dict(sorted(tags.items())), "attributes": dict(sorted(attributes.items())),
                  "fontFamilies": sorted(fonts), "cssFontFamilyDeclarations": sorted(css_fonts),
                  "externalDependencies": sorted(dependencies), "externalReferenceUses": dependency_uses,
                  "embeddedReferences": embedded, "localReferences": local, "eventHandlers": handlers}


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Pass a new output JSON path; no SVG payload files are written.")
    output = Path(sys.argv[1])
    if output.exists():
        raise SystemExit("Preserve the existing inventory; select a new output path.")
    html, status, _, _ = download(BASE)
    index = DirectoryLinks()
    index.feed(html.decode("utf-8"))
    prefix = urllib.parse.urlparse(BASE).path
    names = set()
    for href in index.links:
        link = urllib.parse.urlparse(urllib.parse.urljoin(BASE, href))
        if link.netloc == urllib.parse.urlparse(BASE).netloc and link.path.startswith(prefix) and link.path.lower().endswith(".svg"):
            name = urllib.parse.unquote(link.path[len(prefix):])
            if "/" not in name:
                names.add(name)
    if not names:
        raise SystemExit("No SVG entries found; do not treat an empty listing as qualification.")
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as workers:
        rows = list(workers.map(inspect, sorted(names)))
    result = {"schemaVersion": 1, "directory": BASE, "retrievedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "indexHttpStatus": status, "indexSha256": hashlib.sha256(html).hexdigest(), "indexedFiles": len(names),
              "retrievedFiles": sum(row["retrieval"] == "fetched" for row in rows), "sourceSvgFilesRetained": 0,
              "scope": "Retrieval and XML feature inventory only; library/native rendering validation is pending.", "files": rows}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key != "files"}))


if __name__ == "__main__":
    main()
