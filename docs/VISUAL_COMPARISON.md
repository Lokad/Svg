# Exact visual comparison

Run the independent comparator with two PNG paths:

```powershell
dotnet run --project tools/Lokad.Svg.VisualDiff -- reference.png candidate.png
```

It exits `0` only for same-sized, exactly equal canonical RGBA8 pixels; it exits
`1` for a visual mismatch and `2` for usage or input-decoding errors. Output is a
JSON `PixelComparison` record with changed-pixel count, maximum/mean/RMS error,
separate alpha error, and mismatch bounds. Byte-identical PNG compression is not
required. RGB values at alpha zero are canonicalized to zero; all partial-alpha
differences are failures.

The current decoder intentionally accepts bounded, non-interlaced RGB8 and RGBA8
PNG output observed from the initial Chrome recipe. Decoder-format expansion must retain CRC and
decoded-size validation and must not use the production image decoder as proof.
