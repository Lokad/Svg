"""Regenerate pinned default Unicode case data; source downloads stay in RAM."""
from pathlib import Path
import hashlib
import json
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
BASE = "https://www.unicode.org/Public/17.0.0/ucd/"
EXPECTED = {"UnicodeData.txt": "2e1efc1dcb59c575eedf5ccae60f95229f706ee6d031835247d843c11d96470c",
            "SpecialCasing.txt": "efc25faf19de21b92c1194c111c932e03d2a5eaf18194e33f1156e96de4c9588",
            "DerivedCoreProperties.txt": "24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08",
            "auxiliary/WordBreakProperty.txt": "72274cac1e6b919507db35655c3e175aa27274668a1ece95c28d2069f2ad9852",
            "PropList.txt": "130dcddcaadaf071008bdfce1e7743e04fdfbc910886f017d9f9ac931d8c64dd"}


def main():
    sources = {}
    for name, digest in EXPECTED.items():
        with urllib.request.urlopen(BASE + name, timeout=60) as response:
            payload = response.read(8 * 1024 * 1024)
        if hashlib.sha256(payload).hexdigest() != digest:
            raise ValueError("Pinned Unicode input hash changed: " + name)
        sources[name] = payload
    mappings = {"Uppercase": {}, "Lowercase": {}, "Titlecase": {}}
    combining = {}
    for row in sources["UnicodeData.txt"].decode("utf-8").splitlines():
        fields = row.split(";")
        if int(fields[3]):
            combining[int(fields[0], 16)] = int(fields[3])
        for operation, index in [("Uppercase", 12), ("Lowercase", 13)]:
            if fields[index]:
                mappings[operation][int(fields[0], 16)] = (int(fields[index], 16),)
        title = fields[14] or fields[12]
        if title:
            mappings["Titlecase"][int(fields[0], 16)] = (int(title, 16),)
    final_sigma = False
    for row in sources["SpecialCasing.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 5:
            continue
        if fields[4].strip() == "Final_Sigma":
            assert int(fields[0], 16) == 0x3A3 and fields[1].split() == ["03C2"]
            final_sigma = True
        if not fields[4].strip():
            for operation, index in [("Uppercase", 3), ("Lowercase", 1), ("Titlecase", 2)]:
                mappings[operation][int(fields[0], 16)] = tuple(int(x, 16) for x in fields[index].split())
    assert final_sigma and mappings["Lowercase"][0x3A3] == (0x3C3,)
    properties = {}
    lowercase_scalars = set()
    for row in sources["DerivedCoreProperties.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 2 or fields[1].strip() not in {"Cased", "Case_Ignorable", "Lowercase"}:
            continue
        bounds = fields[0].strip().split("..")
        first, last = int(bounds[0], 16), int(bounds[-1], 16)
        if fields[1].strip() == "Lowercase":
            lowercase_scalars.update(range(first, last + 1))
            continue
        flag = 1 if fields[1].strip() == "Cased" else 2
        for scalar in range(first, last + 1):
            properties[scalar] = properties.get(scalar, 0) | flag
    for row in sources["PropList.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 2 or fields[1].strip() != "Soft_Dotted":
            continue
        bounds = fields[0].strip().split("..")
        for scalar in range(int(bounds[0], 16), int(bounds[-1], 16) + 1):
            assert scalar not in combining
            properties[scalar] = properties.get(scalar, 0) | 4
    for scalar, canonical_class in combining.items():
        properties[scalar] = properties.get(scalar, 0) | 8 | (16 if canonical_class == 230 else 0)
    # CSS capitalization leaves an already-uppercase first letter unchanged.
    # Four uppercase digraphs have nonidentity Unicode titlecase mappings.
    titlecase = mappings.pop("Titlecase")
    mappings["Capitalization"] = {key: value for key, value in titlecase.items() if key in lowercase_scalars}
    ranges = []
    for scalar, flags in sorted(properties.items()):
        if ranges and ranges[-1][1] + 1 == scalar and ranges[-1][2] == flags:
            ranges[-1][1] = scalar
        else:
            ranges.append([scalar, scalar, flags])
    # Only classes affecting a cased letter's word start are needed. Every
    # non-identity titlecase scalar is ALetter, except the ignorable U+0345.
    word_classes = {"ALetter": 1, "Hebrew_Letter": 1, "Numeric": 2,
                    "ExtendNumLet": 3, "MidLetter": 4, "MidNumLet": 4,
                    "Single_Quote": 4, "Extend": 5, "Format": 5, "ZWJ": 5}
    words = {}
    for row in sources["auxiliary/WordBreakProperty.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 2 or fields[1].strip() not in word_classes:
            continue
        bounds = fields[0].strip().split("..")
        first, last = int(bounds[0], 16), int(bounds[-1], 16)
        for scalar in range(first, last + 1):
            words[scalar] = word_classes[fields[1].strip()]
    word_ranges = []
    for scalar, value in sorted(words.items()):
        if word_ranges and word_ranges[-1][1] + 1 == scalar and word_ranges[-1][2] == value:
            word_ranges[-1][1] = scalar
        else:
            word_ranges.append([scalar, scalar, value])
    assert all(words.get(key) == (5 if key == 0x345 else 1)
               for key, value in mappings["Capitalization"].items() if value != (key,))
    bank = ROOT / "tests/Research/text-case-mapping"
    lines = ["// Generated by tools/generate-unicode-case-data.py from Unicode 17.0.0.",
             "// Copyright Unicode, Inc. See THIRD_PARTY_NOTICES.txt (Unicode License V3).",
             "namespace Lokad.Svg.Parsing;", "", "internal static partial class SvgTextCasing", "{"]
    statistics = {}
    for operation, values in mappings.items():
        values = {key: value for key, value in sorted(values.items()) if value != (key,)}
        simple = [(key, value[0]) for key, value in values.items() if len(value) == 1]
        expansions = [(key, value) for key, value in values.items() if len(value) > 1]
        assert all(key <= 0xFFFF and all(scalar <= 0xFFFF for scalar in value) for key, value in expansions)
        maximum = max(len("".join(map(chr, value)).encode("utf-16-le")) // 2 for value in values.values())
        assert maximum <= 3
        lines += [f"    private static ReadOnlySpan<int> {operation}Pairs =>", "    ["]
        for offset in range(0, len(simple), 5):
            lines.append("        " + ", ".join(f"0x{key:X}, 0x{value:X}" for key, value in simple[offset:offset + 5]) + ",")
        lines += ["    ];", "", f"    private static ReadOnlySpan<int> {operation}ExpansionRows =>", "    ["]
        offset, all_units = 0, []
        for key, value in expansions:
            lines.append(f"        0x{key:X}, {offset}, {len(value)},")
            offset += len(value)
            all_units.append("".join(f"\\u{x:04X}" for x in value))
        lines += ["    ];", "", f"    private static ReadOnlySpan<char> {operation}ExpansionText =>"]
        for index in range(0, len(all_units), 5):
            end = ";" if index + 5 >= len(all_units) else " +"
            lines.append('        "' + "".join(all_units[index:index + 5]) + '"' + end)
        lines += [""]
        (bank / ("unicode-" + operation.lower() + ".csv")).write_bytes(("source;" + operation.lower() + "\n" + "".join(f"{key:X};{' '.join(f'{x:X}' for x in value)}\n" for key, value in values.items())).encode("ascii"))
        statistics[operation.lower()] = dict(changedScalars=len(values), simpleMappings=len(simple), expansions=len(expansions), maximumEmittedUtf16UnitsPerScalar=maximum)
    lines += ["    private static ReadOnlySpan<int> CasingPropertyRanges =>", "    ["]
    for first, last, flags in ranges:
        lines.append(f"        0x{first:X}, 0x{last:X}, {flags},")
    lines += ["    ];", "", "    private static ReadOnlySpan<int> CasingWordRanges =>", "    ["]
    for first, last, value in word_ranges:
        lines.append(f"        0x{first:X}, 0x{last:X}, {value},")
    lines += ["    ];", "}", ""]
    (ROOT / "src/Lokad.Svg/Parsing/SvgTextCasing.Data.cs").write_bytes("\n".join(lines).encode("utf-8"))
    (bank / "unicode-casing-properties.csv").write_bytes(("first;last;flags\n" + "".join(f"{a:X};{b:X};{c:X}\n" for a, b, c in ranges)).encode("ascii"))
    (bank / "unicode-casing-words.csv").write_bytes(("first;last;class\n" + "".join(f"{a:X};{b:X};{c}\n" for a, b, c in word_ranges)).encode("ascii"))
    metadata = dict(unicodeVersion="17.0.0", sources={name: dict(url=BASE + name, sha256=hashlib.sha256(payload).hexdigest()) for name, payload in sources.items()}, **statistics["uppercase"], lowercase=statistics["lowercase"], capitalization=statistics["capitalization"], casingPropertyRanges=len(ranges), casingPropertyFlags=dict(Cased=1, Case_Ignorable=2, Soft_Dotted=4, NonStarter=8, Above=16), casingWordRanges=len(word_ranges), wordClasses=word_classes, conditionalMaximumEmittedUtf16UnitsPerScalar=3, language="Default/English and Unicode conditional Turkic/Lithuanian mappings. Additional content-language tailoring and mark positioning remain unfinished.")
    (bank / "unicode-data.json").write_bytes((json.dumps(metadata, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(metadata))


if __name__ == "__main__":
    main()
