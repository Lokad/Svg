using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows immutable scene coefficients for color operations retained until image sampling.</summary>
/// <remarks>The scene outlives every raster consumer. This value owns no arrays or reservations;
/// graph indices are validated earlier-result links. Evaluation visits at most the bounded filter graph,
/// and its caller owns pixel-loop cancellation. Individual clamps and color transfers remain ordered.</remarks>
internal readonly record struct SvgFilterColorProgram(ReadOnlyMemory<SvgFilterPrimitive> Primitives,
    ReadOnlyMemory<SvgFilterInput> MergeInputs, int FirstStage, int LastStage,
    SvgFilterColorSpace ResourceSpace, SvgFilterColorSpace SourceSpace)
{
    internal SvgShaderPaint Apply(SvgShaderPaint input, SvgFilterColorSpace destination)
    {
        SvgShaderPaint output = Evaluate(this, LastStage, input, out SvgFilterColorSpace space);
        return space == destination ? output : SvgFilterColorTransfer.Convert(output, destination);

        static SvgShaderPaint Evaluate(in SvgFilterColorProgram program, int index, SvgShaderPaint input,
            out SvgFilterColorSpace space)
        {
            SvgFilterPrimitive primitive = program.Primitives.Span[index];
            SvgShaderPaint paint;
            if (index == program.FirstStage) { paint = input; space = program.SourceSpace; }
            else
            {
                int parent = primitive.Kind == SvgFilterPrimitiveKind.Merge && primitive.MergeCount == 1
                    ? program.MergeInputs.Span[primitive.MergeFirst].Index : primitive.Input.Index;
                if ((uint)parent >= (uint)index || parent < program.FirstStage)
                    throw new SvgException(SvgErrorCode.MalformedInput, "Pending color operations require earlier graph stages.");
                paint = Evaluate(program, parent, input, out space);
            }
            SvgFilterColorSpace working = primitive.ColorSpace ?? program.ResourceSpace;
            if (working != space) paint = SvgFilterColorTransfer.Convert(paint, working);
            if (primitive.Kind == SvgFilterPrimitiveKind.ColorMatrix) paint = primitive.ColorMatrix.Apply(paint);
            space = working;
            return paint;
        }
    }
}
