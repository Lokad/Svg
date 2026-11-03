using System.Text;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Admits one DOCTYPE and internal general entities consistently across XML consumers.</summary>
internal static class SvgXmlReaderPolicy
{
    private const int PrologWorkspaceBytes = 32 * 1024;

    /// <remarks>The caller retains immutable input and its ordinary reader reservation through every reader's completion. This method grows that reservation for internal declarations and expanded reader storage before the BCL parses them. It owns only temporary prolog storage. Scans check cancellation in bounded chunks, retain no entity names or replacement text, create no DOM or whole-input string, and never resolve a resource. Independent calls are thread safe. Other subset declarations and duplicate DOCTYPEs reject before the main reader can skip them.</remarks>
    internal static XmlReaderSettings Create(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget,
        RenderBudget.BudgetReservation readerWorkspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        DtdProcessing processing;
        try { processing = ValidateProlog(svg, limits.MaximumInputBytes, budget, readerWorkspace, cancellationToken); }
        catch (DecoderFallbackException) { throw Malformed(); }
        return new() { DtdProcessing = processing, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes,
            MaxCharactersFromEntities = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };

        static DtdProcessing ValidateProlog(ReadOnlyMemory<byte> svg, int maximumCharacters, RenderBudget budget,
            RenderBudget.BudgetReservation readerWorkspace, CancellationToken token)
        {
            ReadOnlySpan<byte> bytes = svg.Span;
            (int stride, bool bigEndian, int offset) = Detect(bytes);
            int start = offset;
            while (IsSpace(Unit(bytes, start, stride, bigEndian)))
            {
                if ((start - offset) % 4096 == 0) token.ThrowIfCancellationRequested();
                start += stride;
            }
            if (Unit(bytes, start, stride, bigEndian) != '<' || Unit(bytes, start + stride, stride, bigEndian) is not ('?' or '!')) return DtdProcessing.Prohibit;

            using RenderBudget.BudgetReservation reservation = budget.Reserve(PrologWorkspaceBytes, SvgMemoryCategory.Parser);
            System.Text.Encoding encoding = stride switch { 2 => bigEndian ? System.Text.Encoding.BigEndianUnicode : System.Text.Encoding.Unicode, 4 => new UTF32Encoding(bigEndian, true, true), _ => System.Text.Encoding.UTF8 };
            // Let the BCL interpret the XML declaration's encoding, including aliases
            // and supplied encoding providers. Compact only unquoted whitespace:
            // the BCL materializes this node, so padding must not grow its storage.
            // Preserve the BOM, byte encoding and quoted values for BCL validation.
            if (start == offset && Match(bytes, start, stride, bigEndian, "<?xml") && IsSpace(Unit(bytes, start + 5 * stride, stride, bigEndian)))
            {
                int length = CompactDeclaration(bytes, stride, bigEndian, offset, Span<byte>.Empty, token);
                // The fixed workspace covers ordinary declarations and reader buffers.
                // Longer tokens retain their grammar and get an explicit allowance for
                // the compact bytes and the BCL's decoded node and growth buffers.
                if (length > 1024) reservation.Grow(16L * length);
                byte[] compact = new byte[length];
                CompactDeclaration(bytes, stride, bigEndian, offset, compact, token);
                using ReadOnlyMemoryStream probeStream = new(compact);
                using XmlTextReader probe = new(probeStream) { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                if (!probe.Read() || probe.NodeType != XmlNodeType.XmlDeclaration) throw Malformed();
                encoding = probe.Encoding ?? throw Malformed();
            }

            using ReadOnlyMemoryStream stream = new(svg);
            using StreamReader text = new(stream, encoding, true, 1024, false);
            PrologCursor cursor = new(text, token);
            DtdProcessing mode = DtdProcessing.Prohibit;
            int declarations = 0, subsetCharacters = 0, maximumLiteral = 0, maximumReferences = 0;
            int next = cursor.Read();
            if (next == 0xfeff) next = cursor.Read();
            while (true)
            {
                while (IsSpace(next)) next = cursor.Read();
                if (next != '<') break;
                next = cursor.Read();
                if (next == '?') { cursor.SkipUntil("?>"); next = cursor.Read(); continue; }
                if (next != '!') break;
                next = cursor.Read();
                if (next == '-')
                {
                    if (cursor.Read() != '-') break;
                    cursor.SkipUntil("-->"); next = cursor.Read(); continue;
                }
                if (next != 'D' || !MatchText(ref cursor, "OCTYPE")) break;
                if (mode != DtdProcessing.Prohibit) throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML contains more than one DOCTYPE declaration.");
                mode = DtdProcessing.Ignore;
                int quote = 0;
                while (true)
                {
                    next = cursor.Read();
                    if (next < 0) throw Malformed();
                    if (quote != 0) { if (next == quote) quote = 0; continue; }
                    if (next is '\'' or '"') quote = next;
                    else if (next == '[')
                    {
                        int subsetStart = cursor.Position;
                        (declarations, maximumLiteral, maximumReferences) = ReadSubset(ref cursor);
                        subsetCharacters = cursor.Position - subsetStart;
                        mode = DtdProcessing.Parse;
                        do { next = cursor.Read(); } while (IsSpace(next));
                        if (next != '>') throw Malformed();
                        break;
                    }
                    else if (next == '>') break;
                }
                next = cursor.Read();
            }

            if (mode == DtdProcessing.Parse)
            {
                // Count possible uses in the body, including harmless lookalikes in
                // comments/CDATA. Do not resolve names or materialize replacement text.
                int references = 0;
                if (declarations != 0)
                {
                    for (; next >= 0; next = cursor.Read()) if (next == '&') references++;
                }
                long expanded = ExpansionBound(declarations, maximumLiteral, maximumReferences, references, maximumCharacters);
                // The ordinary reservation still covers source-sized reader buffers.
                // Retain coarse allowances for DTD tables/literals and expanded value
                // buffers (UTF-16 growth/copy overlap), not just final output strings.
                readerWorkspace.Grow(16 * 1024L + 8L * subsetCharacters + 1024L * declarations + 8L * expanded);
            }
            token.ThrowIfCancellationRequested();
            return mode;

            static (int Declarations, int MaximumLiteral, int MaximumReferences) ReadSubset(ref PrologCursor input)
            {
                int count = 0, literal = 0, references = 0;
                while (true)
                {
                    int value;
                    do { value = input.Read(); } while (IsSpace(value));
                    if (value == ']') return (count, literal, references);
                    if (value != '<') throw Malformed();
                    value = input.Read();
                    if (value == '?') { input.SkipUntil("?>"); continue; }
                    if (value != '!') throw Malformed();
                    value = input.Read();
                    if (value == '-')
                    {
                        if (input.Read() != '-') throw Malformed();
                        input.SkipUntil("-->");
                        continue;
                    }
                    if (value != 'E' || !MatchText(ref input, "NTITY") || !IsSpace(input.Read())) throw SubsetNotAdmitted();
                    do { value = input.Read(); } while (IsSpace(value));
                    if (value < 0 || value is '%' or '\'' or '"' or '>') throw SubsetNotAdmitted();
                    // Names and replacement grammar are validated by the BCL. Only
                    // classify internal literal declarations here; no name table.
                    while (!IsSpace(value))
                    {
                        if (value < 0 || value is '\'' or '"' or '>') throw Malformed();
                        value = input.Read();
                    }
                    do { value = input.Read(); } while (IsSpace(value));
                    if (value is not ('\'' or '"')) throw SubsetNotAdmitted();
                    int quote = value, length = 0, amps = 0;
                    while ((value = input.Read()) != quote)
                    {
                        if (value < 0) throw Malformed();
                        length++;
                        if (value == '&') amps++;
                    }
                    do { value = input.Read(); } while (IsSpace(value));
                    if (value != '>') throw Malformed();
                    count++;
                    literal = Math.Max(literal, length);
                    references = Math.Max(references, amps);
                }

                static SvgException SubsetNotAdmitted() => new(SvgErrorCode.MalformedInput,
                    "Only internal general entity declarations are admitted in an SVG XML subset.");
            }

            static long ExpansionBound(int declarations, int literal, int references, int uses, int ceiling)
            {
                // A valid active reference path has at most N declared entities:
                // cycles reject in the BCL. Raw literal length bounds normalized
                // length, and each raw ampersand can introduce at most one reference.
                // L * (1 + R + ... + R^(N-1)) therefore bounds one expansion and
                // intermediate replacement text. Numeric ampersands are included.
                // Saturation only bounds storage admission; it never rejects input.
                long bound = 0;
                if (references <= 1) bound = Math.Min(ceiling, (long)literal * (references == 0 ? Math.Min(declarations, 1) : declarations));
                else for (int depth = 0; depth < declarations && bound < ceiling; depth++)
                    bound = Math.Min(ceiling, literal + references * bound);
                return Math.Min(ceiling, uses * bound);
            }

            static bool Match(ReadOnlySpan<byte> input, int position, int unit, bool be, ReadOnlySpan<char> expected)
            {
                foreach (char value in expected) { if (Unit(input, position, unit, be) != value) return false; position += unit; }
                return true;
            }

            static int CompactDeclaration(ReadOnlySpan<byte> input, int stride, bool bigEndian, int offset,
                Span<byte> output, CancellationToken token)
            {
                int length = offset;
                if (!output.IsEmpty) input[..offset].CopyTo(output);
                int quote = 0;
                bool previousSpace = false;
                for (int position = offset; input.Length - position >= stride; position += stride)
                {
                    if ((position - offset) % 4096 == 0)
                    {
                        SvgXmlPrologProbe.Checkpoint(position - offset);
                        token.ThrowIfCancellationRequested();
                    }
                    int value = Unit(input, position, stride, bigEndian);
                    bool space = quote == 0 && IsSpace(value);
                    if (!(space && previousSpace))
                    {
                        if (!output.IsEmpty) input.Slice(position, stride).CopyTo(output[length..]);
                        length += stride;
                    }
                    previousSpace = space;
                    if (quote != 0) { if (value == quote) quote = 0; }
                    else if (value is '\'' or '"') quote = value;
                    else if (value == '?' && Unit(input, position + stride, stride, bigEndian) == '>')
                    {
                        if (!output.IsEmpty) input.Slice(position + stride, stride).CopyTo(output[length..]);
                        return length + stride;
                    }
                }
                throw Malformed();
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

    private struct PrologCursor(StreamReader reader, CancellationToken token)
    {
        private int _position;
        internal int Position => _position;
        internal int Read()
        {
            if (_position % 4096 == 0)
            {
                SvgXmlPrologProbe.Checkpoint(_position);
                token.ThrowIfCancellationRequested();
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
