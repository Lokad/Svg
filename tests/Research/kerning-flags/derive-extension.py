"""Reproduce the CC0 GPOS-extension/hint fixture with fontTools 4.66.1."""
import argparse
import hashlib
import io
from pathlib import Path
import fontTools
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import otTables
from fontTools.ttLib.tables.ttProgram import Program

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, required=True)
arguments = parser.parse_args()
if arguments.output.exists():
    parser.error("Choose a new output file.")
if fontTools.version != "4.66.1":
    parser.error("Use fontTools 4.66.1 to reproduce the recorded program hash.")
source = Path(__file__).resolve().parent / "fonts/GposMarks.ttf"
if hashlib.sha256(source.read_bytes()).hexdigest() != "3ddd4f153cb98d19992a33869a2c3eadcb22c1c6c671f8c8d474685b23f92051":
    raise ValueError("The original CC0 font changed.")
font = TTFont(source, recalcTimestamp=False)
geometry = {name: font["glyf"][name].getCoordinates(font["glyf"]) for name in font.getGlyphOrder()}
for lookup in font["GPOS"].table.LookupList.Lookup:
    original_type = lookup.LookupType
    wrapped = []
    for payload in lookup.SubTable:
        extension = otTables.ExtensionPos()
        extension.Format = 1
        extension.ExtensionLookupType = original_type
        extension.ExtSubTable = payload
        wrapped.append(extension)
    lookup.LookupType = 9
    lookup.SubTable = wrapped
for glyph in font["glyf"].glyphs.values():
    if glyph.numberOfContours:
        glyph.program = Program()
        # Copy Y projection into freedom, then restore both axes to X.
        # Direct VM mechanisms test the observable movement and retained state.
        glyph.program.fromBytecode([0x02, 0x05, 0x0E, 0x01])
font["maxp"].maxSizeOfInstructions = 4
font["maxp"].maxStackElements = 1
for record in font["name"].names:
    if record.nameID in {1, 3, 4, 6}:
        font["name"].setName("GposExtension", record.nameID, record.platformID, record.platEncID, record.langID)
buffer = io.BytesIO()
font.save(buffer)
data = buffer.getvalue()
readback = TTFont(io.BytesIO(data))
for name, expected in geometry.items():
    if readback["glyf"][name].getCoordinates(readback["glyf"]) != expected:
        raise ValueError("Original glyph geometry changed: " + name)
arguments.output.parent.mkdir(parents=True, exist_ok=True)
arguments.output.write_bytes(data)
print(hashlib.sha256(data).hexdigest(), len(data))
