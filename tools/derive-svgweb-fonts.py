#!/usr/bin/env python3
"""Derive disposable exact-outline TrueType transports for seven SVGWeb fonts.

Research only. Requires fontTools 4.66.1; downloads SVG bodies into bounded RAM.
No hosted SVG or font is installed, retained as XML, or added to ordinary tests.
"""

import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

import fontTools
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.basePen import BasePen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont

sys.dont_write_bytecode = True


class Segments(BasePen):
    """Compare explicit contours, excluding only redundant closing lines."""

    def __init__(self, glyphset):
        super().__init__(glyphset)
        self.commands = []
        self.start = None

    def _moveTo(self, point):
        self.start = point
        self.commands.append(("M", point))

    def _lineTo(self, point):
        self.commands.append(("L", point))

    def _qCurveToOne(self, control, end):
        self.commands.append(("Q", control, end))

    def _curveToOne(self, first, second, end):
        self.commands.append(("C", first, second, end))

    def _closePath(self):
        if self.commands and self.commands[-1] == ("L", self.start):
            self.commands.pop()
        self.commands.append(("Z",))

    def _endPath(self):
        self.commands.append(("E",))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-directory", required=True, type=Path)
    arguments = parser.parse_args()
    if fontTools.version != "4.66.1":
        parser.error("Use fontTools 4.66.1 to reproduce recorded program hashes.")
    repo = Path(__file__).resolve().parents[1]
    destination = arguments.output_directory.resolve()
    if not destination.is_relative_to(repo / "artifacts"):
        parser.error("Choose a new disposable directory under artifacts/.")
    destination.mkdir(parents=True, exist_ok=False)
    specification = importlib.util.spec_from_file_location("inventory", repo / "tools/inventory-svgweb.py")
    if specification is None or specification.loader is None:
        raise RuntimeError("Cannot load the bounded inventory downloader.")
    inventory = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(inventory)
    ledger = json.loads((repo / "tests/Research/svgweb/originals.json").read_text())
    entries = {entry["name"]: entry for entry in ledger["samples"]}
    names = ["DroidSans-Bold", "DroidSans", "DroidSansMono", "DroidSerif-Bold",
             "DroidSerif-BoldItalic", "DroidSerif-Italic", "DroidSerif-Regular"]
    rows = []
    for name in names:
        entry = entries[name + ".svg"]
        body, *_ = inventory.download(entry["url"])
        if hashlib.sha256(body).hexdigest() != entry["sourceSha256"]:
            raise ValueError("Hosted source hash changed: " + name)
        root = ET.fromstring(body)
        font = next(element for element in root.iter() if element.tag == "{http://www.w3.org/2000/svg}font")
        face = font.find("{http://www.w3.org/2000/svg}font-face")
        missing = font.find("{http://www.w3.org/2000/svg}missing-glyph")
        if face is None or missing is None:
            raise ValueError("Missing font descriptors or default glyph.")
        if face.get("font-weight") is not None or face.get("font-style") is not None:
            raise ValueError("Unexpected nondefault SVG face descriptors.")
        if any(element.tag.rsplit("}", 1)[-1] not in {"font-face", "glyph", "missing-glyph"} for element in font):
            raise ValueError("This transport does not invent kerning or other font behavior.")
        glyphs = font.findall("{http://www.w3.org/2000/svg}glyph")
        order = [".notdef"]
        cmap, outlines, sources, metrics = {}, {}, {}, {}
        for glyph in [missing, *glyphs]:
            scalar = ord(glyph.get("unicode")) if glyph is not missing else None
            identity = ".notdef" if scalar is None else "uni" + format(scalar, "04X")
            if scalar is not None:
                cmap[scalar] = identity
                order.append(identity)
            normalized = Segments(None)
            parse_path(glyph.get("d", ""), normalized)
            if any(command[0] not in {"M", "L", "Q", "Z"} for command in normalized.commands):
                raise ValueError("Only the original quadratic contours are transported.")
            if any(not float(value).is_integer() for command in normalized.commands for point in command[1:] for value in point):
                raise ValueError("Transport would round source coordinates.")
            pen = TTGlyphPen(None)
            parse_path(glyph.get("d", ""), pen)
            outlines[identity] = pen.glyph()
            advance = float(glyph.get("horiz-adv-x", font.get("horiz-adv-x")))
            if not advance.is_integer():
                raise ValueError("Transport would round a source advance.")
            sources[identity] = (normalized.commands, int(advance))
        builder = FontBuilder(int(face.get("units-per-em")), isTTF=True)
        builder.setupGlyphOrder(order)
        builder.setupCharacterMap(cmap)
        builder.setupGlyf(outlines)
        for identity, glyph in outlines.items():
            glyph.recalcBounds(builder.font["glyf"])
            metrics[identity] = (sources[identity][1], getattr(glyph, "xMin", 0))
        ascent, descent = int(face.get("ascent")), int(face.get("descent"))
        builder.setupHorizontalMetrics(metrics)
        builder.setupHorizontalHeader(ascent=ascent, descent=descent, lineGap=0)
        family = "SVGWeb " + name
        builder.setupNameTable(dict(familyName=family, styleName="Regular", uniqueFontIdentifier="SVGWebExact-" + entry["sourceSha256"], fullName=family, psName="SVGWeb-" + name, version="Version 1.000"))
        builder.setupOS2(sTypoAscender=ascent, sTypoDescender=descent, sTypoLineGap=0, usWinAscent=ascent, usWinDescent=-descent, usWeightClass=400, fsSelection=0x40)
        builder.setupPost()
        builder.setupMaxp()
        builder.font["head"].created = builder.font["head"].modified = 2082844800
        builder.font.recalcTimestamp = False
        buffer = io.BytesIO()
        builder.font.save(buffer)
        data = buffer.getvalue()
        readback = TTFont(io.BytesIO(data))
        glyphset = readback.getGlyphSet()
        for identity in order:
            normalized = Segments(glyphset)
            glyphset[identity].draw(normalized)
            if normalized.commands != sources[identity][0] or readback["hmtx"].metrics[identity][0] != sources[identity][1]:
                raise ValueError("Exact outline/advance round trip failed: " + identity)
        digest = hashlib.sha256(data).hexdigest()
        (destination / (name + ".ttf")).write_bytes(data)
        manifest = dict(packId="svgweb-" + name.lower(), files=[dict(relativePath=name + ".ttf", byteSize=len(data), sha256=digest)], families=[dict(requestedFamily=face.get("font-family"), resolvedFamily=family, relativeFontFile=name + ".ttf", weight=400, italic=False, faceIndex=0, hasMathTable=False)], fallbacks=[])
        (destination / (name + ".json")).write_text(json.dumps(manifest, indent=2) + "\n")
        rows.append(dict(name=name, originalSourceSha256=entry["sourceSha256"], sourceFamily=face.get("font-family"), derivedFamily=family, bytes=len(data), sha256=digest, glyphs=len(glyphs), exactOutlineAndAdvanceRoundTrips=len(order), ascent=ascent, descent=descent, unitsPerEm=readback["head"].unitsPerEm))
    report = dict(fontToolsVersion=fontTools.version, sourceSvgFilesRetained=0, rows=rows)
    (destination / "programs.json").write_text(json.dumps(report, indent=2) + "\n")
    print("Derived seven disposable font transports; verified all 672 glyph round trips. No SVG bodies retained.")


if __name__ == "__main__":
    main()
