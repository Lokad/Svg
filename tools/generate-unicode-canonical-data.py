"""Regenerate the pinned canonical-shaping data; downloaded UCD inputs stay in RAM."""
from pathlib import Path
import hashlib
import json
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
BASE = "https://www.unicode.org/Public/17.0.0/ucd/"
EXPECTED = {
    "UnicodeData.txt": "2e1efc1dcb59c575eedf5ccae60f95229f706ee6d031835247d843c11d96470c",
    "DerivedNormalizationProps.txt": "71fd6a206a2c0cdd41feb6b7f656aa31091db45e9cedc926985d718397f9e488",
    "DerivedCoreProperties.txt": "24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08",
    "Scripts.txt": "9f5e50d3abaee7d6ce09480f325c706f485ae3240912527e651954d2d6b035bf",
    "IndicSyllabicCategory.txt": "3fc122f4cf58b0c19268d5f810263b04ab4e1e67743386ec0e0ada9c76aec5be",
    "extracted/DerivedJoiningType.txt": "f39ebe974825d6736aee15582250307aa532b2cfab3caf3f86bd23fddc9c5c4d",
    "extracted/DerivedBidiClass.txt": "4867b4b7f0731ed1bfcd34cc6251211ff1542541fce0734b6fbda139ee80b3a4",
    "PropList.txt": "130dcddcaadaf071008bdfce1e7743e04fdfbc910886f017d9f9ac931d8c64dd",
}


def main():
    sources = {}
    for name, digest in EXPECTED.items():
        with urllib.request.urlopen(BASE + name, timeout=45) as response:
            payload = response.read(8 * 1024 * 1024)
        if hashlib.sha256(payload).hexdigest() != digest:
            raise ValueError("Pinned Unicode input hash changed: " + name)
        sources[name] = payload
    classes, marks, decompositions = {}, set(), {}
    for row in sources["UnicodeData.txt"].decode("utf-8").splitlines():
        fields = row.split(";")
        scalar = int(fields[0], 16)
        if int(fields[3]):
            classes[scalar] = int(fields[3])
        if fields[2].startswith("M"):
            marks.add(scalar)
        if fields[5] and not fields[5].startswith("<"):
            decompositions[scalar] = tuple(int(x, 16) for x in fields[5].split())
    excluded = set()
    for row in sources["DerivedNormalizationProps.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 2 or fields[1].strip() != "Full_Composition_Exclusion":
            continue
        bounds = fields[0].strip().split("..")
        excluded.update(range(int(bounds[0], 16), int(bounds[-1], 16) + 1))
    compositions = sorted((parts[0], parts[1], scalar) for scalar, parts in decompositions.items()
                          if len(parts) == 2 and scalar not in excluded)
    assert len({(a, b) for a, b, _ in compositions}) == len(compositions)

    def decompose(scalar):
        if scalar not in decompositions:
            return (scalar,)
        return tuple(x for part in decompositions[scalar] for x in decompose(part))

    expanded = {scalar: decompose(scalar) for scalar in sorted(decompositions)}
    ignorables = []
    for row in sources["DerivedCoreProperties.txt"].decode("utf-8").splitlines():
        fields = row.split("#", 1)[0].strip().split(";")
        if len(fields) < 2 or fields[1].strip() != "Default_Ignorable_Code_Point":
            continue
        bounds = fields[0].strip().split("..")
        first, last = int(bounds[0], 16), int(bounds[-1], 16)
        if ignorables and ignorables[-1][1] + 1 == first:
            ignorables[-1][1] = last
        else:
            ignorables.append([first, last])
    maximum = max(map(len, expanded.values()))
    assert maximum == 4

    def records(payload):
        for line in payload.decode("utf-8").splitlines():
            fields = line.split("#", 1)[0].strip().split(";")
            if len(fields) < 2:
                continue
            bounds = fields[0].strip().split("..")
            yield int(bounds[0], 16), int(bounds[-1], 16), fields[1].strip()

    # This is admission data for the existing finite Latin shaping contract,
    # not an implementation of bidi, joining or syllabic layout. Hangul's jamo
    # shaping route is excluded too; canonical Unicode primitives remain global.
    scripts = ["Unknown"] * 0x110000
    for first, last, script in records(sources["Scripts.txt"]):
        scripts[first:last + 1] = [script] * (last - first + 1)
    complex_scripts = {"Hangul"}
    for first, last, category in records(sources["IndicSyllabicCategory.txt"]):
        if category.startswith("Consonant") or category.startswith("Vowel"):
            complex_scripts.update(scripts[first:last + 1])
    for first, last, joining in records(sources["extracted/DerivedJoiningType.txt"]):
        if joining in {"D", "R", "L"}:
            complex_scripts.update(scripts[first:last + 1])
    complex_scripts.difference_update({"Common", "Inherited", "Unknown"})
    bidi = bytearray(0x110000)
    # Missing defaults include reserved RTL ranges. Assigned records override
    # those defaults (for example Arabic digits and nonspacing marks).
    for line in sources["extracted/DerivedBidiClass.txt"].decode("utf-8").splitlines():
        if "@missing:" not in line:
            continue
        span, category = line.split("@missing:", 1)[1].strip().split(";")
        bounds = span.strip().split("..")
        first, last = int(bounds[0], 16), int(bounds[-1], 16)
        value = int(category.strip() in {"Right_To_Left", "Arabic_Letter"})
        bidi[first:last + 1] = bytes([value]) * (last - first + 1)
    for first, last, category in records(sources["extracted/DerivedBidiClass.txt"]):
        bidi[first:last + 1] = bytes([int(category in {"R", "AL"})]) * (last - first + 1)
    for first, last, category in records(sources["PropList.txt"]):
        if category == "Bidi_Control":
            bidi[first:last + 1] = bytes([1]) * (last - first + 1)
    requirements = {scalar: (512 if bidi[scalar] else 0) | (1024 if script in complex_scripts else 0)
                    for scalar, script in enumerate(scripts) if bidi[scalar] or script in complex_scripts}
    assert min(requirements) >= 0x300
    ranges = []
    for scalar in sorted(marks | classes.keys() | requirements.keys()):
        value = classes.get(scalar, 0) | (256 if scalar in marks else 0) | requirements.get(scalar, 0)
        if ranges and ranges[-1][1] + 1 == scalar and ranges[-1][2] == value:
            ranges[-1][1] = scalar
        else:
            ranges.append([scalar, scalar, value])
    assert min(marks) == 0x300 and min(classes) == 0x300 and ignorables[0][0] == 0xAD
    lines = ["// Generated by tools/generate-unicode-canonical-data.py from Unicode 17.0.0.",
             "// Copyright Unicode, Inc. See THIRD_PARTY_NOTICES.txt (Unicode License V3).",
             "namespace Lokad.Svg.Fonts;", "", "internal static partial class SvgCanonicalText", "{"]
    for name, rows in [("CanonicalProperties", ranges), ("CanonicalCompositions", compositions), ("DefaultIgnorableRanges", ignorables)]:
        lines += [f"    private static ReadOnlySpan<int> {name} =>", "    ["]
        for row in rows:
            lines.append("        " + ", ".join(f"0x{x:X}" for x in row) + ",")
        lines += ["    ];", ""]
    lines += ["    private static ReadOnlySpan<int> CanonicalDecompositions =>", "    ["]
    for scalar, parts in expanded.items():
        lines.append("        " + ", ".join(f"0x{x:X}" for x in (scalar, len(parts), *parts, *(0 for _ in range(maximum - len(parts))))) + ",")
    lines += ["    ];", "}", ""]
    output = ROOT / "src/Lokad.Svg/Fonts/SvgCanonicalText.Data.cs"
    output.write_bytes("\n".join(lines).encode("utf-8"))
    metadata = dict(unicodeVersion="17.0.0", sources={name: dict(url=BASE + name, sha256=digest) for name, digest in EXPECTED.items()},
                    propertyRanges=len(ranges), canonicalDecompositions=len(expanded), canonicalCompositions=len(compositions), defaultIgnorableRanges=len(ignorables),
                    maximumDecomposedScalars=maximum, hangul="Algorithmic canonical decomposition and composition.",
                    layoutAdmission=dict(bidirectionalScalars=sum(bidi), unsupportedScripts=sorted(complex_scripts),
                                         unsupportedScriptScalars=sum(script in complex_scripts for script in scripts),
                                         packedFlags=dict(mark=256, bidirectional=512, complexScript=1024),
                                         policy="Reject R/AL, explicit bidi controls and joining/syllabic scripts outside the finite Latin layout subset. Common/inherited characters retain their separate canonical treatment."),
                    dataLfSha256=hashlib.sha256(output.read_bytes()).hexdigest())
    bank = ROOT / "tests/Research/text-case-mapping"
    (bank / "unicode-canonical-data.json").write_bytes((json.dumps(metadata, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(metadata))


if __name__ == "__main__":
    main()
