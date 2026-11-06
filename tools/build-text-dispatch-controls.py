"""Rebuild authored text-dispatch programs; fontTools is a development dependency."""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import otTables as ot
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
import argparse
import hashlib
import io
import json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--include-noto', action='store_true')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    original = (root / 'tests/Research/kerning-precedence/fonts/PlainKern.ttf').read_bytes()
    assert hashlib.sha256(original).hexdigest() == '0526c14b283c2bc05eecdc9677615a32f12d27c881fbc1ac52759716828076a7'
    cases = ['ccmp', 'context', 'normalization', 'fallback']
    if args.include_noto:
        cases.append('normalization-noto')
    receipts = []
    for identity in cases:
        source = (root / 'tests/Research/supplied-font-labels/fonts/NotoSans-Regular.ttf').read_bytes() if identity == 'normalization-noto' else original
        if identity == 'normalization-noto':
            assert hashlib.sha256(source).hexdigest() == 'b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5'
        font = TTFont(io.BytesIO(source), recalcTimestamp=False)
        cmap = font.getBestCmap()
        a, v = cmap[65], cmap[86]
        if identity == 'ccmp':
            addOpenTypeFeaturesFromString(font, 'feature ccmp { sub ' + a + ' by ' + v + '; } ccmp;')
        elif identity == 'context':
            addOpenTypeFeaturesFromString(font, 'lookup Adv { pos ' + a + ' -100; } Adv; feature kern { pos ' + a + "' lookup Adv " + v + '; } kern;')
            # Fix one class-chained format-2 rule, independently of feaLib's
            # size-based choice between equivalent context encodings.
            lookups = font['GPOS'].table.LookupList.Lookup
            assert [item.LookupType for item in lookups] == [1, 8]
            context = ot.ChainContextPos()
            context.Format = 2
            context.Coverage = ot.Coverage()
            context.Coverage.glyphs = [a]
            for name, classes in [('BacktrackClassDef', {}), ('InputClassDef', {a: 1, v: 2}), ('LookAheadClassDef', {})]:
                value = ot.ClassDef()
                value.classDefs = classes
                setattr(context, name, value)
            rule = ot.ChainPosClassRule()
            rule.BacktrackGlyphCount, rule.Backtrack = 0, []
            rule.InputGlyphCount, rule.Input = 2, [2]
            rule.LookAheadGlyphCount, rule.LookAhead = 0, []
            action = ot.PosLookupRecord()
            action.SequenceIndex, action.LookupListIndex = 0, 0
            rule.PosCount, rule.PosLookupRecord = 1, [action]
            rules = ot.ChainPosClassSet()
            rules.ChainPosClassRuleCount, rules.ChainPosClassRule = 1, [rule]
            context.ChainPosClassSetCount, context.ChainPosClassSet = 3, [None, rules, None]
            lookups[1].SubTableCount, lookups[1].SubTable = 1, [context]
        elif identity == 'normalization':
            for table in font['cmap'].tables:
                if table.isUnicode():
                    table.cmap[0x301] = v
            addOpenTypeFeaturesFromString(font, 'markClass ' + v + ' <anchor 0 0> @TOP; feature mark { pos base ' + a + ' <anchor 40 650> mark @TOP; } mark;')
        elif identity == 'fallback':
            for table in font['cmap'].tables:
                if table.isUnicode():
                    table.cmap[0xC1] = v
        else:
            for table in font['cmap'].tables:
                if table.isUnicode():
                    table.cmap.pop(0xC1, None)
        buffer = io.BytesIO()
        font.save(buffer)
        program = buffer.getvalue()
        verification = TTFont(io.BytesIO(program), recalcTimestamp=False)
        source_font = TTFont(io.BytesIO(source), recalcTimestamp=False)
        for tag in ['glyf', 'hmtx', 'hhea', 'maxp', 'loca', 'prep', 'fpgm', 'cvt ']:
            if tag in source_font:
                assert source_font.getTableData(tag) == verification.getTableData(tag), (identity, tag)
        folder = args.output / identity
        folder.mkdir(parents=True, exist_ok=True)
        (folder / 'font.ttf').write_bytes(program)
        receipts.append(dict(identity=identity, sourceSha256=hashlib.sha256(source).hexdigest(), byteSize=len(program), sha256=hashlib.sha256(program).hexdigest()))
    print(json.dumps(receipts, indent=2))


if __name__ == '__main__':
    main()
