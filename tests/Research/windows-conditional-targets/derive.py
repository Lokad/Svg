"""Rebuild the original CC0 programs; optional development-only fontTools 4.66.1."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import fontTools
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import newTable
from fontTools.ttLib.tables.ttProgram import Program

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output-directory", type=Path, required=True)
output = parser.parse_args().output_directory
if fontTools.__version__ != "4.66.1":
    raise RuntimeError("Use the recorded fontTools 4.66.1 development dependency.")
output.mkdir(parents=True)
data = json.loads(Path(__file__).with_name("native-targets.json").read_text(encoding="utf-8"))
pen = TTGlyphPen(None)
pen.moveTo(tuple(data["sourcePoints"][0]))
for point in data["sourcePoints"][1:]:
    pen.lineTo(tuple(point))
pen.closePath()
plain = pen.glyph()

for entry in data["fonts"]:
    glyphs = {".notdef": TTGlyphPen(None).glyph(), "plain": copy.deepcopy(plain)}
    for case in entry["cases"]:
        if case["composite"]:
            pen = TTGlyphPen(glyphs)
            pen.addComponent("plain", (1, 0, 0, 1, 0, 0))
            glyph = pen.glyph()
            glyph.components[0].flags |= 0x200
        else:
            glyph = copy.deepcopy(plain)
        operation = bytes.fromhex(case["operation"])
        assembly = ["SVTCA[0]" if case["axis"] == "y" else "SVTCA[1]",
                    "PUSHB[ ]", "0", str(case["sourcePoint"]), "GC[0]",
                    "PUSHW[ ]", "4096", "MUL[ ]", "WS[ ]", "SVTCA[0]"]
        for point in range(4):
            assembly += ["PUSHB[ ]", str(point), "0", "RS[ ]"]
            if point in (1, 2):
                assembly += ["PUSHW[ ]", "256", "ADD[ ]"]
            assembly += ["SCFS[ ]"]
        assembly += ["SVTCA[1]"]
        for point in range(4):
            assembly += ["PUSHW[ ]", str(point), "1024" if point in (2, 3) else "0", "SCFS[ ]"]
        assembly += ["PUSHB[ ]", "0", "3", "FLIPRGON[ ]", "PUSHB[ ]", "0", "SCANCTRL[ ]",
                     "PUSHB[ ]", "2", "SCANTYPE[ ]"]
        tail = Program()
        tail.fromAssembly(assembly)
        glyph.program = Program()
        glyph.program.fromBytecode(operation + tail.getBytecode())
        assert len(glyphs) == case["glyph"]
        glyphs[case["id"]] = glyph
    for paint in entry["paintGlyphs"]:
        glyph = copy.deepcopy(glyphs[paint["id"]])
        operation = next(case["operation"] for case in entry["cases"] if case["id"] == paint["id"])
        glyph.program = Program()
        glyph.program.fromBytecode(bytes.fromhex(operation))
        assert len(glyphs) == paint["glyph"]
        glyphs["paint-" + paint["id"]] = glyph

    order = list(glyphs)
    builder = FontBuilder(data["unitsPerEm"], isTTF=True)
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({65 + index: name for index, name in enumerate(order[2:])})
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (1024, 0) for name in order})
    builder.setupHorizontalHeader(ascent=2048, descent=-512)
    builder.setupNameTable({"familyName": "ConditionalTouch", "styleName": "Regular",
                           "uniqueFontIdentifier": "Original-Conditional-Touch-" + entry["profile"],
                           "fullName": "ConditionalTouch", "psName": "ConditionalTouch",
                           "licenseDescription": "CC0 1.0 Universal"})
    builder.setupOS2(sTypoAscender=2048, sTypoDescender=-512, usWinAscent=2048, usWinDescent=512)
    builder.setupPost()
    builder.setupMaxp()
    maximum = builder.font["maxp"]
    maximum.maxStorage, maximum.maxFunctionDefs, maximum.maxStackElements, maximum.maxZones = 1, 1, 16, 2
    maximum.maxSizeOfInstructions = max(len(glyph.program.getBytecode()) for glyph in glyphs.values()
                                        if hasattr(glyph, "program"))
    builder.font["head"].flags = 9
    builder.font["head"].created = builder.font["head"].modified = 2082844800
    builder.font.recalcTimestamp = False
    fpgm = newTable("fpgm")
    fpgm.program = Program()
    fpgm.program.fromBytecode(bytes.fromhex("b0002c" + data["conditionalFunction"] + "2d"))
    builder.font["fpgm"] = fpgm
    gasp = newTable("gasp")
    gasp.version, gasp.gaspRange = 1, {65535: 15 if entry["profile"] == "symmetric" else 7}
    builder.font["gasp"] = gasp
    path = output / entry["font"]
    path.parent.mkdir(parents=True, exist_ok=True)
    builder.save(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != entry["fontSha256"]:
        raise RuntimeError("The reconstructed program differs: " + path.name)
    print(json.dumps({"font": path.name, "sha256": digest, "bytes": path.stat().st_size}))
