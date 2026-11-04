#!/usr/bin/env python3
"""Rebuild the OFL numerical conic font; output is explicitly caller-selected."""

import argparse
import json
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import newTable


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    bank = Path(__file__).resolve().parent.parent / "tests/Research/glyph-conic-centers"
    data = json.loads((bank / "native-centers.json").read_text(encoding="utf-8"))
    order = [".notdef"] + ["v" + str(i) for i in range(len(data["cases"]))]
    pen = TTGlyphPen(None)
    glyphs = {".notdef": pen.glyph()}
    metrics = {".notdef": (2048, 0)}
    for name, case in zip(order[1:], data["cases"]):
        segments = case["segments"]
        pen = TTGlyphPen(None)
        pen.moveTo(tuple(segments[0]["start"]))
        for segment in segments:
            if segment["control"] == segment["end"]:
                pen.lineTo(tuple(segment["end"]))
            else:
                pen.qCurveTo(tuple(segment["control"]), tuple(segment["end"]))
        pen.closePath()
        glyphs[name] = pen.glyph()
        metrics[name] = (2048, min(s[key][0] for s in segments for key in ("start", "control", "end")))
    builder = FontBuilder(2048, isTTF=True)
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({65 + i: name for i, name in enumerate(order[1:])})
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=1800, descent=-248)
    builder.setupOS2(sTypoAscender=1800, sTypoDescender=-248, usWinAscent=1800, usWinDescent=248)
    family = "Lokad SVG Conic Centers"
    builder.setupNameTable({"familyName": family, "styleName": "Regular",
        "uniqueFontIdentifier": family + " Regular", "fullName": family + " Regular",
        "psName": "LokadSvgConicCenters-Regular"})
    builder.setupPost()
    builder.setupMaxp()
    gasp = newTable("gasp")
    gasp.version = 1
    gasp.gaspRange = {65535: 10}
    builder.font["gasp"] = gasp
    builder.font["head"].created = 3406620153
    builder.font["head"].modified = 3406620153
    builder.font.recalcTimestamp = False
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    builder.save(arguments.output)


if __name__ == "__main__":
    main()
