"""Rebuild the original CC0 controls using development-only fontTools 4.66.1."""
import argparse
import copy
import hashlib
import io
import json
from pathlib import Path

import fontTools
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables.ttProgram import Program

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output-directory", type=Path, required=True)
output = parser.parse_args().output_directory
if fontTools.__version__ != "4.66.1":
    raise RuntimeError("Use the recorded fontTools 4.66.1 development dependency.")
output.mkdir(parents=True)
record = json.loads(Path(__file__).with_name("native-masks.json").read_text(encoding="utf-8"))

for entry in record["fonts"]:
    marker = entry["file"].startswith("fonts/Small")
    profile = entry["profile"]
    pen = TTGlyphPen(None)
    polygons = [[(0, 0), (0, 512), (512, 512), (512, 0)]] if marker else [
        [(23, 0), (23, 64), (24, 64), (24, 0)],
        [(0, 128), (0, 192), (64, 192), (64, 128)]]
    for points in polygons:
        pen.moveTo(points[0])
        for point in points[1:]:
            pen.lineTo(point)
        pen.closePath()
    glyph = pen.glyph()
    program = Program()
    if marker:
        assembly = ["SVTCA[0]"]
        for point in range(4):
            assembly += ["PUSHW[ ]", str(point), "896" if point in (1, 2) else "640", "SCFS[ ]"]
        assembly += ["SVTCA[1]"]
        for point in range(4):
            assembly += ["PUSHW[ ]", str(point), "256" if point in (2, 3) else "0", "SCFS[ ]"]
        assembly += ["PUSHB[ ]", "0", "SCANCTRL[ ]", "PUSHB[ ]", "2", "SCANTYPE[ ]"]
        program.fromAssembly(assembly)
    else:
        program.fromBytecode(bytes.fromhex("b00021"))
    glyph.program = program
    family = "Original tiny hint control" if marker else "Original default cutoff"
    builder = FontBuilder(2048, isTTF=True)
    builder.setupGlyphOrder([".notdef", "control"])
    builder.setupCharacterMap({65: "control"})
    builder.setupGlyf({".notdef": TTGlyphPen(None).glyph(), "control": glyph})
    builder.setupHorizontalMetrics({name: (1024 if marker else 2048, 0) for name in [".notdef", "control"]})
    builder.setupHorizontalHeader(ascent=2048, descent=-512 if marker else -64)
    builder.setupNameTable({"familyName": family, "styleName": "Regular",
        "uniqueFontIdentifier": "OriginalTinyHint" + profile if marker else profile + "32",
        "fullName": family, "psName": "OriginalTinyHint" if marker else "OriginalDefaultCutoff",
        "licenseDescription": "CC0 1.0 Universal"})
    builder.setupOS2(sTypoAscender=2048, sTypoDescender=-512 if marker else -64,
        usWinAscent=2048, usWinDescent=512 if marker else 64)
    builder.setupPost()
    builder.setupMaxp()
    builder.font["maxp"].maxStackElements = 4
    builder.font["maxp"].maxSizeOfInstructions = len(program.getBytecode())
    builder.font["maxp"].maxZones = 2
    builder.font["head"].flags = 1
    builder.font["head"].created = builder.font["head"].modified = 2082844800
    builder.font.recalcTimestamp = False
    gasp = newTable("gasp")
    gasp.version = 1
    gasp.gaspRange = {65535: 7 if profile == "natural" else 15}
    builder.font["gasp"] = gasp
    buffer = io.BytesIO()
    builder.save(buffer)
    font = TTFont(io.BytesIO(buffer.getvalue()), recalcTimestamp=False)
    font["head"].flags = font["head"].flags | 8 if entry["integerSize"] else font["head"].flags & ~8
    order = font.getGlyphOrder()
    if marker:
        quarter = copy.deepcopy(font["glyf"]["control"])
        quarter.program = Program()
        quarter.program.fromBytecode(b"")
        order.append("quarter")
        font["glyf"]["quarter"] = quarter
        font["hmtx"]["quarter"] = font["hmtx"]["control"]
        specs = [("parent", "quarter", 1), ("scaled-child", "control", .5)]
    else:
        threshold = copy.deepcopy(font["glyf"]["control"])
        threshold.program = Program()
        threshold.program.fromAssembly(["PUSHW[ ]", "288", "SCANCTRL[ ]", "PUSHB[ ]", "4", "SCANTYPE[ ]"])
        order.append("threshold")
        font["glyf"]["threshold"] = threshold
        font["hmtx"]["threshold"] = font["hmtx"]["control"]
        font["maxp"].maxSizeOfInstructions = len(threshold.program.getBytecode())
        specs = [("half-default", "control", .5)]
    for name, component, scale in specs:
        pen = TTGlyphPen(font["glyf"])
        pen.addComponent(component, (scale, 0, 0, scale, 0, 0))
        composite = pen.glyph()
        composite.components[0].flags |= 512
        if name == "parent":
            composite.program = copy.deepcopy(program)
        order.append(name)
        font["glyf"][name] = composite
        font["hmtx"][name] = font["hmtx"]["control"]
    font.setGlyphOrder(order)
    if marker:
        for table in font["cmap"].tables:
            if table.isUnicode():
                table.cmap.update({66: "parent", 67: "scaled-child"})
    path = output / Path(entry["file"]).name
    font.save(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != entry["sha256"]:
        raise RuntimeError(f"Reproduction differs for {path.name}: {digest}")
    print(f"{path.name}: {digest}")
