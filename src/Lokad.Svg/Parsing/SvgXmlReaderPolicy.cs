using System.Text;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Admits one inert DOCTYPE declaration consistently across XML consumers.</summary>
internal static class SvgXmlReaderPolicy
{
    private const int PrologWorkspaceBytes = 32 * 1024;

    /// <remarks>The caller retains immutable input and reserves its ordinary XML-reader workspace. This method owns and releases a fixed prolog workspace before returning settings. Prolog traversals charge the caller's work counter and check cancellation in bounded chunks. It creates no DOM or whole-input string, never resolves a resource, and is safe for concurrent independent calls. Internal subsets and duplicate declarations are rejected before the main reader can skip them.</remarks>
    internal static XmlReaderSettings Create(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        bool declaration;
        try { declaration = ValidateProlog(svg, budget, workCounter, cancellationToken); }
        catch (DecoderFallbackException) { throw Malformed(); }
        return new() { DtdProcessing = declaration ? DtdProcessing.Ignore : DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };

        static bool ValidateProlog(ReadOnlyMemory<byte> svg, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken token)
        {
            ReadOnlySpan<byte> bytes = svg.Span;
            (int stride, bool bigEndian, int offset) = Detect(bytes);
            int start = offset;
            while (IsSpace(Unit(bytes, start, stride, bigEndian)))
            {
                if ((start - offset) % ParserWorkCounter.ByteCheckpointSize == 0) workCounter.Consume(token);
                start += stride;
            }
            if (Unit(bytes, start, stride, bigEndian) != '<' || Unit(bytes, start + stride, stride, bigEndian) is not ('?' or '!')) return false;

            using RenderBudget.BudgetReservation reservation = budget.Reserve(PrologWorkspaceBytes, SvgMemoryCategory.Parser);
            System.Text.Encoding encoding = stride switch { 2 => bigEndian ? System.Text.Encoding.BigEndianUnicode : System.Text.Encoding.Unicode, 4 => new UTF32Encoding(bigEndian, true, true), _ => System.Text.Encoding.UTF8 };
            // Let the BCL interpret the XML declaration's encoding, including aliases
            // and supplied encoding providers. Prohibit DTDs during this single-node
            // probe, which runs only when an XML declaration starts the document.
            if (start == offset && Match(bytes, start, stride, bigEndian, "<?xml") && IsSpace(Unit(bytes, start + 5 * stride, stride, bigEndian)))
            {
                int end = start;
                int quote = 0;
                while (end < bytes.Length)
                {
                    if ((end - start) % ParserWorkCounter.ByteCheckpointSize == 0) workCounter.Consume(token);
                    int value = Unit(bytes, end, stride, bigEndian);
                    if (quote != 0) { if (value == quote) quote = 0; }
                    else if (value is '\'' or '"') quote = value;
                    else if (value == '?' && Unit(bytes, end + stride, stride, bigEndian) == '>') break;
                    end += stride;
                }
                using ReadOnlyMemoryStream probeStream = new(svg);
                using XmlTextReader probe = new(probeStream) { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                if (!probe.Read() || probe.NodeType != XmlNodeType.XmlDeclaration) throw Malformed();
                encoding = probe.Encoding ?? throw Malformed();
            }

            using ReadOnlyMemoryStream stream = new(svg);
            using StreamReader text = new(stream, encoding, true, 1024, false);
            PrologCursor cursor = new(text, workCounter, token);
            bool found = false;
            int next = cursor.Read();
            if (next == 0xfeff) next = cursor.Read();
            while (true)
            {
                while (IsSpace(next)) next = cursor.Read();
                if (next != '<') return found;
                next = cursor.Read();
                if (next == '?') { cursor.SkipUntil("?>"); next = cursor.Read(); continue; }
                if (next != '!') return found;
                next = cursor.Read();
                if (next == '-')
                {
                    if (cursor.Read() != '-') return found;
                    cursor.SkipUntil("-->"); next = cursor.Read(); continue;
                }
                if (next != 'D' || !MatchText(ref cursor, "OCTYPE")) return found;
                if (found) throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML contains more than one DOCTYPE declaration.");
                found = true;
                int quote = 0;
                while (true)
                {
                    next = cursor.Read();
                    if (next < 0) throw Malformed();
                    if (quote != 0) { if (next == quote) quote = 0; continue; }
                    if (next is '\'' or '"') quote = next;
                    else if (next == '[') throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML internal DTD subsets are not admitted.");
                    else if (next == '>') break;
                }
                next = cursor.Read();
            }

            static bool Match(ReadOnlySpan<byte> input, int position, int unit, bool be, ReadOnlySpan<char> expected)
            {
                foreach (char value in expected) { if (Unit(input, position, unit, be) != value) return false; position += unit; }
                return true;
            }

            static bool MatchText(ref PrologCursor input, ReadOnlySpan<char> expected)
            {
                foreach (char value in expected) if (input.Read() != value) return false;
                return true;
            }

            static (int Stride, bool BigEndian, int Offset) Detect(ReadOnlySpan<byte> input)
            {
                if (input.Length >= 4 && input[0] == 0xff && input[1] == 0xfe && input[2] == 0 && input[3] == 0) return (4, false, 4);
                if (input.Length >= 4 && input[0] == 0 && input[1] == 0 && input[2] == 0xfe && input[3] == 0xff) return (4, true, 4);
                if (input.Length >= 2 && input[0] == 0xff && input[1] == 0xfe) return (2, false, 2);
                if (input.Length >= 2 && input[0] == 0xfe && input[1] == 0xff) return (2, true, 2);
                if (input.Length >= 3 && input[0] == 0xef && input[1] == 0xbb && input[2] == 0xbf) return (1, false, 3);
                if (input.Length >= 4)
                {
                    if (input[0] == '<' && input[1] == 0 && input[2] == 0 && input[3] == 0) return (4, false, 0);
                    if (input[0] == 0 && input[1] == 0 && input[2] == 0 && input[3] == '<') return (4, true, 0);
                    if (input[0] == '<' && input[1] == 0 && input[3] == 0) return (2, false, 0);
                    if (input[0] == 0 && input[1] == '<' && input[2] == 0) return (2, true, 0);
                }
                return (1, false, 0);
            }
        }
    }

    private static int Unit(ReadOnlySpan<byte> bytes, int offset, int stride, bool bigEndian)
    {
        if (offset < 0 || bytes.Length - offset < stride) return -1;
        int value = 0;
        for (int index = 0; index < stride; index++) value |= bytes[offset + index] << (8 * (bigEndian ? stride - index - 1 : index));
        return value;
    }

    private static bool IsSpace(int value) => value is ' ' or '\t' or '\r' or '\n';
    private static SvgException Malformed() => new(SvgErrorCode.MalformedInput, "SVG XML prolog is malformed.");

    private struct PrologCursor(StreamReader reader, ParserWorkCounter counter, CancellationToken token)
    {
        private int _position;
        internal int Read()
        {
            if (_position % ParserWorkCounter.ByteCheckpointSize == 0)
            {
                SvgXmlPrologProbe.Checkpoint(_position);
                counter.Consume(token);
            }
            _position++;
            return reader.Read();
        }
        internal void SkipUntil(ReadOnlySpan<char> ending)
        {
            int previous = -1;
            int beforePrevious = -1;
            while (true)
            {
                int value = Read();
                if (value < 0) throw Malformed();
                if (value == ending[^1] && previous == ending[^2] && (ending.Length == 2 || beforePrevious == ending[0])) return;
                beforePrevious = previous;
                previous = value;
            }
        }
    }
}
