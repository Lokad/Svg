using System.Reflection;
using System.Buffers;
using System.Runtime.InteropServices;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class PublicContractTests
{
    [Fact]
    public void Font_pack_public_contract_requires_explicit_selection_and_cancellation_parameters()
    {
        MethodInfo parse = typeof(SvgFontPackCatalog).GetMethod(nameof(SvgFontPackCatalog.Parse), BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo resolve = typeof(SvgFontPackCatalog).GetMethod(nameof(SvgFontPackCatalog.Resolve), BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo bind = typeof(SvgFontPackCatalog).GetMethod(nameof(SvgFontPackCatalog.Bind), BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo acquire = typeof(SvgFontPackCatalog).GetMethod(nameof(SvgFontPackCatalog.AcquireAsync), BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo getBytes = typeof(ISvgFontProgramSource).GetMethod(nameof(ISvgFontProgramSource.GetBytesAsync), BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo getSource = typeof(ISvgFontProgramSourceProvider).GetMethod(nameof(ISvgFontProgramSourceProvider.GetSourceAsync), BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo[] snapshots = typeof(SvgFontSnapshot).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == nameof(SvgFontSnapshot.CreateAsync)).ToArray();

        Assert.All(parse.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.All(resolve.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.All(bind.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.All(acquire.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));
        ParameterInfo cancellation = Assert.Single(getBytes.GetParameters());
        Assert.Equal(typeof(CancellationToken), cancellation.ParameterType);
        Assert.False(cancellation.HasDefaultValue);
        Assert.Equal(typeof(CancellationToken), getSource.GetParameters()[1].ParameterType);
        Assert.Equal(4, snapshots.Length);
        Assert.All(snapshots.SelectMany(method => method.GetParameters()), parameter => Assert.False(parameter.HasDefaultValue));
    }

    [Fact]
    public void Font_diagnostics_are_bounded_and_do_not_require_source_text()
    {
        SvgFontDiagnostic diagnostic = new(SvgFontFailureStage.Glyph, "Requested", "Resolved", "face:0", string.Empty);

        Assert.Equal(SvgFontFailureStage.Glyph, diagnostic.Stage);
        Assert.Equal("face:0", diagnostic.FaceIdentity);
        SvgFontDiagnostic truncated = new(SvgFontFailureStage.Source, new string('x', 257), string.Empty, string.Empty, string.Empty);
        Assert.Equal(256, truncated.RequestedFamily.Length);
    }

    [Fact]
    public void Explicit_viewports_override_root_outer_dimensions()
    {
        SvgRenderOptions options = new(new SvgViewport(2, 3), 1, SvgColor.Transparent, SvgRenderLimits.Default);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 0, 0, 3 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void Rasterization_stops_before_exceeding_the_configured_work_limit()
    {
        SvgRenderLimits limits = new(200_000, 1024, 16, 16, 1024, 16, 16, 20);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='1'><rect width='16' height='1' fill='red'/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("Rectangle raster work", exception.Message);
    }

    [Fact]
    public void Scaled_stroke_corners_do_not_overflow()
    {
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 32 32'><rect x='4' y='4' width='24' height='24' fill='none' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.True(png.Length > 0);
    }

    [Fact]
    public void Scaled_translucent_stroke_corners_do_not_overflow()
    {
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 32 32'><rect x='4' y='4' width='24' height='24' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.True(png.Length > 0);
    }

    [Fact]
    public void Parser_work_reduces_the_raster_work_allowance()
    {
        SvgRenderLimits limits = new(200_000, 1024, 16, 16, 1024, 16, 16, 5);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><style>rect { fill: red; }</style><rect width='1' height='1'/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("parsing work", exception.Message);
    }

    [Fact]
    public void Parser_syntax_scanning_respects_the_public_shared_work_limit()
    {
        (string Content, int MaximumWorkUnits)[] cases =
        [
            ("<style>" + new string(' ', 4096) + "rect { fill: red; }</style>", 4),
            ("<path d='" + new string(' ', 4096) + "X'/>", 5),
        ];

        foreach ((string content, int maximumWorkUnits) in cases)
        {
            SvgRenderLimits limits = new(20_000, 8_192, 16, 16, 1_024, 16, 16, maximumWorkUnits);
            SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
            string svg = "<svg xmlns='http://www.w3.org/2000/svg'>" + content + "</svg>";

            SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), options, CancellationToken.None));

            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        }
    }

    [Fact]
    public void Xml_reader_traversal_respects_the_public_shared_work_limit()
    {
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 3);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'><desc>bounded metadata</desc></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("parsing work", exception.Message);
    }

    [Fact]
    public void Background_initialization_respects_the_public_shared_work_limit()
    {
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 2);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, new SvgColor(1, 2, 3, 255), limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("Background initialization", exception.Message);
    }

    [Fact]
    public void Png_preprocessing_respects_the_public_shared_work_limit()
    {
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 2);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("PNG preprocessing", exception.Message);
    }

    [Fact]
    public void Encoding_stops_before_exceeding_the_configured_png_limit()
    {
        SvgRenderLimits limits = new(20_000, 1024, 16, 16, 1, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Converter_methods_require_all_parameters()
    {
        MethodInfo[] methods = typeof(SvgConverter).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(6, methods.Length);
        Assert.All(methods.SelectMany(method => method.GetParameters()), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.Equal(3, methods.Count(method => method.Name == "ConvertToPng"));
        Assert.Equal(3, methods.Count(method => method.Name == "ConvertToPngAsync"));
    }

    [Fact]
    public async Task Public_entry_points_reject_null_arguments()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        using MemoryStream streamDestination = new();
        byte[] memoryDestination = new byte[64];
        SvgFontPackCatalog catalog = CreateNullArgumentCatalog();
        NullArgumentFontProvider provider = new();

        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPng), typeof(ReadOnlyMemory<byte>), typeof(SvgRenderOptions), typeof(CancellationToken)), "options", [new ReadOnlyMemory<byte>(svg), null, CancellationToken.None]);
        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPng), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>), typeof(SvgRenderOptions), typeof(CancellationToken)), "options", [new ReadOnlyMemory<byte>(svg), new Memory<byte>(memoryDestination), null, CancellationToken.None]);
        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPng), typeof(ReadOnlyMemory<byte>), typeof(Stream), typeof(SvgRenderOptions), typeof(CancellationToken)), "destination", [new ReadOnlyMemory<byte>(svg), null, SvgRenderOptions.Default, CancellationToken.None]);
        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPng), typeof(ReadOnlyMemory<byte>), typeof(Stream), typeof(SvgRenderOptions), typeof(CancellationToken)), "options", [new ReadOnlyMemory<byte>(svg), streamDestination, null, CancellationToken.None]);
        await AssertTaskNullArgument(ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPngAsync), typeof(ReadOnlyMemory<byte>), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), null, CancellationToken.None]), "options");
        await AssertTaskNullArgument(ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPngAsync), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), new Memory<byte>(memoryDestination), null, CancellationToken.None]), "options");
        await AssertTaskNullArgument(ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPngAsync), typeof(ReadOnlyMemory<byte>), typeof(Stream), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), null, SvgRenderOptions.Default, CancellationToken.None]), "destination");
        await AssertTaskNullArgument(ConvertMethod(typeof(SvgConverter), nameof(SvgConverter.ConvertToPngAsync), typeof(ReadOnlyMemory<byte>), typeof(Stream), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), streamDestination, null, CancellationToken.None]), "options");
        await AssertValueTaskNullArgument<SvgPreparedDocument>(ConvertMethod(typeof(SvgPreparedDocument), nameof(SvgPreparedDocument.CreateAsync), typeof(ReadOnlyMemory<byte>), typeof(SvgFontPackCatalog), typeof(ISvgFontProgramSourceProvider), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), null, provider, SvgRenderOptions.Default, CancellationToken.None]), "catalog");
        await AssertValueTaskNullArgument<SvgPreparedDocument>(ConvertMethod(typeof(SvgPreparedDocument), nameof(SvgPreparedDocument.CreateAsync), typeof(ReadOnlyMemory<byte>), typeof(SvgFontPackCatalog), typeof(ISvgFontProgramSourceProvider), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), catalog, null, SvgRenderOptions.Default, CancellationToken.None]), "provider");
        await AssertValueTaskNullArgument<SvgPreparedDocument>(ConvertMethod(typeof(SvgPreparedDocument), nameof(SvgPreparedDocument.CreateAsync), typeof(ReadOnlyMemory<byte>), typeof(SvgFontPackCatalog), typeof(ISvgFontProgramSourceProvider), typeof(SvgRenderOptions), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), catalog, provider, null, CancellationToken.None]), "options");
        ConstructorInfo optionsConstructor = typeof(SvgRenderOptions).GetConstructors().Single(static c => c.GetParameters().Length == 5);

        try { optionsConstructor.Invoke([null, 1.0, SvgColor.Transparent, null, null]); Assert.Fail("Null limits constructed without an ArgumentNullException."); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentNullException nullArgument)
        {
            Assert.Equal("limits", nullArgument.ParamName);
        }

    }

    [Fact]
    public async Task Catalog_and_snapshot_entry_points_reject_null_arguments()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        SvgFontPackCatalog catalog = CreateNullArgumentCatalog();
        NullArgumentFontProvider provider = new();
        NullArgumentFontSource source = new();
        SvgFontFaceRequest request = new("Example", 400, false);
        SvgFontPackResolution resolution = catalog.Resolve(request);

        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgFontPackCatalog), "Parse", typeof(ReadOnlyMemory<byte>), typeof(string)), "requestedPackId", [new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("{}")), null]);
        AssertSyncNullArgument(catalog, ConvertMethod(typeof(SvgFontPackCatalog), "Resolve", typeof(SvgFontFaceRequest)), "request", [null]);
        AssertSyncNullArgument(catalog, ConvertMethod(typeof(SvgFontPackCatalog), "Bind", typeof(SvgFontPackResolution), typeof(ISvgFontProgramSource)), "resolution", [null, source]);
        AssertSyncNullArgument(catalog, ConvertMethod(typeof(SvgFontPackCatalog), "Bind", typeof(SvgFontPackResolution), typeof(ISvgFontProgramSource)), "source", [resolution, null]);
        AssertSyncNullArgument(catalog, ConvertMethod(typeof(SvgFontPackCatalog), "AcquireAsync", typeof(SvgFontPackResolution), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)), "resolution", [null, provider, CancellationToken.None]);
        AssertSyncNullArgument(catalog, ConvertMethod(typeof(SvgFontPackCatalog), "AcquireAsync", typeof(SvgFontPackResolution), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)), "provider", [resolution, null, CancellationToken.None]);
        await AssertValueTaskNullArgument<SvgFontSnapshot>(ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(System.Collections.Generic.IReadOnlyList<SvgFontProgramResolution>), typeof(CancellationToken)).Invoke(null, [null, CancellationToken.None]), "resolutions");
        await AssertValueTaskNullArgument<SvgFontSnapshot>(ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(SvgFontPackCatalog), typeof(System.Collections.Generic.IReadOnlyList<SvgFontFaceRequest>), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)).Invoke(null, [null, new[] { request }, provider, CancellationToken.None]), "catalog");
        await AssertValueTaskNullArgument<SvgFontSnapshot>(ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(SvgFontPackCatalog), typeof(System.Collections.Generic.IReadOnlyList<SvgFontFaceRequest>), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)).Invoke(null, [catalog, null, provider, CancellationToken.None]), "requests");
        await AssertValueTaskNullArgument<SvgFontSnapshot>(ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(SvgFontPackCatalog), typeof(System.Collections.Generic.IReadOnlyList<SvgFontFaceRequest>), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)).Invoke(null, [catalog, new[] { request }, null, CancellationToken.None]), "provider");
        await AssertValueTaskNullArgument<SvgFontSnapshot>(ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(ReadOnlyMemory<byte>), typeof(SvgFontPackCatalog), typeof(ISvgFontProgramSourceProvider), typeof(CancellationToken)).Invoke(null, [new ReadOnlyMemory<byte>(svg), null, provider, CancellationToken.None]), "catalog");
        AssertSyncNullArgument(null, ConvertMethod(typeof(SvgFontSnapshot), "CreateAsync", typeof(ReadOnlyMemory<byte>), typeof(SvgFontPackCatalog), typeof(ISvgFontProgramSourceProvider), typeof(SvgRenderLimits), typeof(CancellationToken)), "limits", [new ReadOnlyMemory<byte>(svg), catalog, provider, null, CancellationToken.None]);
    }

    private static MethodInfo ConvertMethod(Type type, string name, params Type[] parameterTypes)
    {
        if (type.GetMethod(name, parameterTypes) is not MethodInfo method) throw new InvalidOperationException("Missing method: " + name);
        return method;
    }

    private static void AssertSyncNullArgument(object? target, MethodBase method, string parameterName, object?[] arguments)
    {
        try { method.Invoke(target, arguments); Assert.Fail("Null argument converted without an ArgumentNullException."); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentNullException nullArgument)
        {
            Assert.Equal(parameterName, nullArgument.ParamName);
        }
    }

    private static async Task AssertTaskNullArgument(object? invocation, string parameterName)
    {
        if (invocation is not Task task) throw new InvalidOperationException("Expected a task.");
        ArgumentNullException nullArgument = await Assert.ThrowsAsync<ArgumentNullException>(() => task);
        Assert.Equal(parameterName, nullArgument.ParamName);
    }

    private static async Task AssertValueTaskNullArgument<T>(object? invocation, string parameterName)
    {
        if (invocation is not ValueTask<T> task) throw new InvalidOperationException("Expected a value task.");
        ArgumentNullException nullArgument = await Assert.ThrowsAsync<ArgumentNullException>(async () => await task);
        Assert.Equal(parameterName, nullArgument.ParamName);
    }

    private static SvgFontPackCatalog CreateNullArgumentCatalog()
    {
        return SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes("{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/a.ttf\", \"byteSize\": 1, \"sha256\": \"0000000000000000000000000000000000000000000000000000000000000000\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/a.ttf\" }] }"), "test-pack");
    }

    private sealed class NullArgumentFontSource : ISvgFontProgramSource
    {
        public string StableId => "null-argument-source";

        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    }

    private sealed class NullArgumentFontProvider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => ValueTask.FromResult<ISvgFontProgramSource?>(null);
    }

    [Fact]
    public void Internal_signatures_do_not_use_optional_parameters()
    {
        List<string> violations = new();
        foreach (Type type in typeof(SvgConverter).Assembly.GetTypes())
        {
            foreach (MethodBase method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.HasDefaultValue) violations.Add(type.FullName + "." + method.Name + "(" + parameter.Name + ")");
                }
            }
            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    if (parameter.HasDefaultValue) violations.Add(type.FullName + ".ctor(" + parameter.Name + ")");
                }
            }
        }

        Assert.True(violations.Count == 0, String.Join("; ", violations));
    }

    [Fact]
    public void Prepared_document_conversion_methods_require_all_parameters()
    {
        MethodInfo[] methods = typeof(SvgPreparedDocument).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => method.Name is nameof(SvgPreparedDocument.ConvertToPng) or nameof(SvgPreparedDocument.ConvertToPngAsync)).ToArray();

        Assert.Equal(6, methods.Length);
        Assert.All(methods.SelectMany(method => method.GetParameters()), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.Equal(3, methods.Count(method => method.Name == nameof(SvgPreparedDocument.ConvertToPng)));
        Assert.Equal(3, methods.Count(method => method.Name == nameof(SvgPreparedDocument.ConvertToPngAsync)));
    }

    [Fact]
    public void Default_options_match_the_provisional_plan_limits()
    {
        SvgRenderLimits limits = SvgRenderOptions.Default.Limits;

        Assert.Null(SvgRenderOptions.Default.Viewport);
        Assert.Equal(1, SvgRenderOptions.Default.OutputScale);
        Assert.Equal(268_435_456, limits.MaximumAccountedBytes);
        Assert.Equal(16_777_216, limits.MaximumInputBytes);
        Assert.Equal(16_384, limits.MaximumDimension);
        Assert.Equal(16_777_216, limits.MaximumPixels);
        Assert.Equal(134_217_728, limits.MaximumEncodedPngBytes);
        Assert.Equal(128, limits.MaximumXmlDepth);
        Assert.Equal(256, limits.MaximumAttributesPerElement);
        Assert.Equal(100_000, limits.MaximumNodes);
        Assert.Equal(1_000_000, limits.MaximumPathCommands);
        Assert.Equal(100_000_000, limits.MaximumWorkUnits);
    }

    [Fact]
    public void Render_limits_reject_a_nonpositive_XML_depth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderLimits(1024, 1024, 16, 16, 1024, 16, 16, 16, 0));
    }

    [Fact]
    public void Render_limits_reject_a_nonpositive_attribute_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderLimits(1024, 1024, 16, 16, 1024, 16, 16, 16, 16, 0));
    }

    [Fact]
    public void Render_limit_convenience_constructors_forward_default_nesting_limits()
    {
        SvgRenderLimits eight = new(1024, 1024, 16, 16, 1024, 16, 16, 16);
        SvgRenderLimits nine = new(1024, 1024, 16, 16, 1024, 16, 16, 16, 64);

        Assert.Equal(128, eight.MaximumXmlDepth);
        Assert.Equal(256, eight.MaximumAttributesPerElement);
        Assert.Equal(256, nine.MaximumAttributesPerElement);
    }

    [Theory]
    [InlineData(0, "maximumAccountedBytes")]
    [InlineData(1, "maximumInputBytes")]
    [InlineData(2, "maximumDimension")]
    [InlineData(3, "maximumPixels")]
    [InlineData(4, "maximumEncodedPngBytes")]
    [InlineData(5, "maximumNodes")]
    [InlineData(6, "maximumPathCommands")]
    [InlineData(7, "maximumWorkUnits")]
    [InlineData(8, "maximumXmlDepth")]
    [InlineData(9, "maximumAttributesPerElement")]
    public void Render_limits_reject_nonpositive_values_at_every_position(int position, string parameterName)
    {
        ConstructorInfo constructor = typeof(SvgRenderLimits).GetConstructors().Single(static c => c.GetParameters().Length == 10);
        object?[] arguments = [268_435_456L, 16_777_216, 16_384, 16_777_216, 134_217_728, 100_000, 1_000_000, 100_000_000, 128, 256];
        arguments[position] = position == 0 ? (object)0L : (object)0;

        try { constructor.Invoke(arguments); Assert.Fail("Nonpositive limit constructed without an ArgumentOutOfRangeException."); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException outOfRange)
        {
            Assert.Equal(parameterName, outOfRange.ParamName);
        }
    }

    [Fact]
    public void Render_options_reject_a_nonpositive_or_nonfinite_output_scale()
    {
        Assert.Equal("outputScale", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderOptions(null, 0, SvgColor.Transparent, SvgRenderLimits.Default)).ParamName);
        Assert.Equal("outputScale", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderOptions(null, double.NaN, SvgColor.Transparent, SvgRenderLimits.Default)).ParamName);
    }

    [Theory]
    [InlineData("", 400, typeof(ArgumentException), "family")]
    [InlineData("   ", 400, typeof(ArgumentException), "family")]
    [InlineData("Example", 0, typeof(ArgumentOutOfRangeException), "weight")]
    [InlineData("Example", -1, typeof(ArgumentOutOfRangeException), "weight")]
    public void Font_face_requests_validate_family_and_weight(string family, int weight, Type errorType, string parameterName)
    {
        Exception exception = Assert.Throws(errorType, () => new SvgFontFaceRequest(family, weight, false));

        Assert.Equal(parameterName, ((ArgumentException)exception).ParamName);
    }

    [Fact]
    public void Cancellation_is_observed_before_the_unimplemented_renderer()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => SvgConverter.ConvertToPng(Array.Empty<byte>(), SvgRenderOptions.Default, source.Token));

        Assert.Equal(source.Token, exception.CancellationToken);
    }

    [Fact]
    public void Input_limit_is_checked_before_rendering()
    {
        SvgRenderLimits limits = new(1_024, 1, 1, 1, 1, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(new byte[] { 1, 2 }, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Malformed_root_fails_before_the_unimplemented_renderer()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<not-svg/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Unsupported_overlapping_root_unit_suffix_fails_before_rasterization()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1vmin' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><rect></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'>&undefined;</svg>")]
    public void Malformed_XML_is_categorized_as_malformed_input(string svg)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Trailing_document_content_fails_as_malformed_input()
    {
        SvgException extraElement = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/><extra/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException trailingText = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>trailing"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException nonemptyExtraElement = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect/></svg><extra/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException nonemptyTrailingText = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect/></svg>trailing"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, extraElement.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, trailingText.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, nonemptyExtraElement.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, nonemptyTrailingText.ErrorCode);
    }

    [Fact]
    public void Truncated_inputs_stay_within_the_categorized_error_contract()
    {
        byte[] full = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='r' width='2' height='2' fill='red'/></defs><style>rect { fill: blue; }</style><g opacity='0.5'><use href='#r'/><rect width='1' height='1' fill='rgb(1, 2, 3)'/><text font-family='Example'>Aé</text></g></svg>");

        for (int length = 0; length <= full.Length; length++)
        {
            try { SvgConverter.ConvertToPng(full[..length], SvgRenderOptions.Default, CancellationToken.None); Assert.Fail("Truncated input converted without a categorized failure."); }
            catch (SvgException) { }
        }
    }

    [Fact]
    public void Truncated_discovery_scans_stay_within_the_categorized_error_contract()
    {
        byte[] full = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='r' width='2' height='2' fill='red'/></defs><style>rect { fill: blue; }</style><g opacity='0.5'><use href='#r'/><rect width='1' height='1' fill='rgb(1, 2, 3)'/><text font-family='Example'>Aé</text></g></svg>");

        for (int length = 0; length < full.Length; length++)
        {
            try { SvgFontRequestDiscovery.Discover(full[..length], CancellationToken.None); Assert.Fail("Truncated input discovered without a categorized failure."); }
            catch (SvgException) { }
        }

        SvgException complete = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(full, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, complete.ErrorCode);
    }

    [Fact]
    public void Animated_rectangle_content_is_rejected_as_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Definition_container_paint_is_rejected_before_instancing()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs fill='red'><rect id='tile' width='1' height='1'/></defs><use href='#tile'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Stylesheets_after_rectangle_definitions_are_rejected_before_instancing()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs><rect id='tile' width='1' height='1'/></defs><style>rect { fill: red; }</style><use href='#tile'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Root_identifiers_that_a_stylesheet_could_target_are_rejected()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' id='root'><style>#root { fill: red; }</style><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Stylesheets_targeting_symbol_definitions_are_rejected_before_instancing()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><style>#tile { fill: red; }</style><defs><symbol id='tile'><rect width='1' height='1'/></symbol></defs><use href='#tile'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Extra_content_in_an_instantiated_symbol_is_rejected_like_multi_rectangle_symbols()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs><symbol id='tile'><rect width='1' height='1' fill='red'/><circle cx='0' cy='0' r='1'/></symbol></defs><use href='#tile'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<metadata><style>rect { fill: red; }</style></metadata>")]
    [InlineData("<script><![CDATA[void 0]]></script>")]
    [InlineData("<metadata><script/></metadata>")]
    [InlineData("<animate attributeName='opacity' values='0;1' dur='1s'/>")]
    [InlineData("<animateMotion path='M 0 0 H 1' dur='1s'/>")]
    [InlineData("<animateTransform attributeName='transform' type='translate' from='0' to='1' dur='1s'/>")]
    [InlineData("<set attributeName='display' to='none'/>")]
    public void Active_content_outside_the_supported_root_stylesheet_is_rejected_before_it_can_be_ignored(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'>" + content + "<rect width='1' height='1'/></svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<line x1='0' y1='0' x2='1' y2='0' stroke='red' stroke-width='2'><animate attributeName='stroke' values='red;blue' dur='1s'/></line>")]
    [InlineData("<path d='M 0 0 H 1 V 1 H 0 Z' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></path>")]
    [InlineData("<polygon points='0,0 1,0 1,1 0,1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></polygon>")]
    [InlineData("<polyline points='0,0 1,0 1,1 0,1 0,0' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></polyline>")]
    [InlineData("<defs><rect id='tile' width='1' height='1' fill='red'/></defs><use href='#tile'><animate attributeName='x' values='0;1' dur='1s'/></use>")]
    [InlineData("<defs><rect id='tile' width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></defs><use href='#tile'/>")]
    [InlineData("<defs><symbol id='tile'><rect width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></symbol></defs><use href='#tile'/>")]
    public void Animated_content_on_other_supported_leaf_elements_is_rejected_as_unsupported(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'>" + content + "</svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<rect width='2' height='2' fill='red'/>")]
    [InlineData("<line x1='0' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/>")]
    [InlineData("<path d='M 0 0 H 2 V 2 H 0 Z' fill='red'/>")]
    [InlineData("<polygon points='0,0 2,0 2,2 0,2' fill='red'/>")]
    [InlineData("<polyline points='0,0 2,0 2,2 0,2' fill='red'/>")]
    [InlineData("<ellipse cx='1' cy='1' rx='1' ry='1' fill='red' stroke='red'/>")]
    [InlineData("<defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile'/>")]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' width='1' height='1'/>")]
    public void Every_currently_supported_paintable_leaf_reaches_public_PNG_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
    }

    [Theory]
    [InlineData("<circle cx='1' cy='1' r='1' fill='url(#g)'/>")]
    [InlineData("<text x='0' y='1'>x</text>")]
    [InlineData("<foreignObject width='1' height='1'><body/></foreignObject>")]
    public void Unimplemented_visible_children_are_rejected_at_public_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<rect vector-effect='non-scaling-stroke'/>")]
    [InlineData("<style>circle { fill: red; }</style><rect/>")]
    [InlineData("<style>rect { fill: red</style><rect/>")]
    [InlineData("<rect x='0.1'/>")]
    [InlineData("<foreign:rect xmlns:foreign='urn:foreign' width='1' height='1'><foreign:child/></foreign:rect>")]
    [InlineData("<g opacity='0.5'><g opacity='0.5'><rect/></g></g>")]
    public void Unsupported_presentation_namespace_and_syntax_forms_are_categorized_at_public_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Nonfinite_color_syntax_is_categorized_as_malformed_public_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect fill='hsl(NaN 50% 50%)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<style>.path { fill: red; opacity: 0.5; }</style><path class='path' d='M 0 0 H 2 V 2 H 0 Z'/>")]
    [InlineData("<style>.accent { stroke: red; }</style><line class='accent' x1='0' y1='1' x2='2' y2='1' stroke-width='2'/>")]
    [InlineData("<style>.layer { fill: red; }</style><g class='layer'><rect width='2' height='2'/></g>")]
    [InlineData("<style>#link { fill: red; }</style><a id='link'><rect width='2' height='2'/></a>")]
    [InlineData("<style>#tile { fill: red; }</style><defs><rect id='tile' width='2' height='2'/></defs><use href='#tile'/>")]
    [InlineData("<style>#instance { opacity: 0.5; }</style><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use id='instance' href='#tile'/>")]
    [InlineData("<style>rect { stroke-width: 1; }</style><rect/>")]
    public void Supported_stylesheet_and_reference_paths_reach_public_PNG_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    public void Root_presentation_hides_direct_local_use_instances(string rootPresentation)
    {
        ReadOnlyMemory<byte> actual = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' " + rootPresentation + "><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use href='#tile'/></svg>"), SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> transparent = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(transparent.ToArray(), actual.ToArray());
    }

    [Fact]
    public void Empty_explicitly_sized_svg_returns_png_bytes()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
    }

    [Fact]
    public void Dimensionless_svg_uses_the_css_default_object_size()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 1, 44, 0, 0, 0, 150 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void One_root_dimension_uses_the_view_box_intrinsic_ratio()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='20' viewBox='0 0 2 1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 20, 0, 0, 0, 10 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void Admits_a_rectangle_view_box_mapping_with_non_quarter_output_edges()
    {
        // R37: the rasterizer evaluates general fractional edges, so a third-pixel
        // rectangle covers exactly one ninth of its pixel.
        byte[] input = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' viewBox='0 0 3 3'>" +
            "<rect width='1' height='1' fill='red'/></svg>");
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 1, 0, 0, 0, 1 }, png.Span.Slice(16, 8).ToArray());
        Assert.Equal(new SvgColor(255, 0, 0, 28), DecodeSinglePixel(png));
    }

    private static SvgColor DecodeSinglePixel(ReadOnlyMemory<byte> png)
    {
        using System.IO.MemoryStream compressed = new();
        for (int offset = 8; offset < png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(offset, 4));
            bool isData = png.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8);
            if (isData) compressed.Write(png.Span.Slice(offset + 8, length));
            offset += length + 12;
        }

        compressed.Position = 0;
        using System.IO.Compression.ZLibStream zlib = new(compressed, System.IO.Compression.CompressionMode.Decompress);
        byte[] raw = new byte[5];
        zlib.ReadExactly(raw);
        if (raw[0] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
        return new SvgColor(raw[1], raw[2], raw[3], raw[4]);
    }

    [Fact]
    public void Rejects_a_nonfinite_view_box_device_transform_as_a_limit()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' viewBox='0 0 1e-320 1' preserveAspectRatio='none'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><rect/><rect/></svg>", 1, 16, 16)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g><rect/></g></svg>", 16, 2, 16)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>", 16, 16, 2)]
    public void Configured_structural_limits_fail_at_public_conversion(string svg, int maximumNodes, int maximumXmlDepth, int maximumAttributes)
    {
        SvgRenderLimits limits = new(32_768, 1_024, 16, 256, 4_096, maximumNodes, 16, 1_000, maximumXmlDepth, maximumAttributes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Cumulative_path_command_limit_fails_at_public_conversion()
    {
        SvgRenderLimits limits = new(32_768, 1_024, 16, 256, 4_096, 16, 9, 1_000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 1 V 1 H 0 Z'/><path d='M 2,0 H 3 V 1 H 2 Z'/></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Surface_budget_fails_before_a_large_pixel_buffer_allocation()
    {
        SvgRenderLimits limits = new(20_000, 1024, 100, 10_000, 1024, 1, 1, 100);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Contiguous_rgba_surface_overflow_is_a_configured_limit_failure()
    {
        SvgRenderLimits limits = new(10_000_000_000, 1024, int.MaxValue, int.MaxValue, 1024, 1, 1, 100);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1073741824' height='2'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Underflowed_output_dimension_is_an_unsupported_conversion()
    {
        SvgRenderOptions options = new(new SvgViewport(double.Epsilon, 1), 0.5, SvgColor.Transparent, SvgRenderLimits.Default);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Owned_png_output_stays_within_the_conversion_budget()
    {
        SvgRenderLimits insufficient = new(131_071, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(131_072, 1024, 2, 4, 1024, 1, 1, 32);
        SvgRenderOptions sufficientOptions = new(null, 1, SvgColor.Transparent, sufficient);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, insufficientOptions, CancellationToken.None));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, sufficientOptions, CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(70, png.Length);
    }

    [Fact]
    public void Retained_scene_arrays_stay_within_the_conversion_budget()
    {
        long retainedScenePeak = ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4) + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4);
        SvgRenderLimits insufficient = new(131_072 + retainedScenePeak - 1, 1024, 2, 4, 1024, 16, 16, 32);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(131_072 + retainedScenePeak, 1024, 2, 4, 1024, 16, 16, 64);
        SvgRenderOptions sufficientOptions = new(null, 1, SvgColor.Transparent, sufficient);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1'/></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, insufficientOptions, CancellationToken.None));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, sufficientOptions, CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(70, png.Length);
    }

    [Fact]
    public void Caller_destination_does_not_reserve_owned_png_output()
    {
        SvgRenderLimits limits = new(131_072, 1024, 2, 4, 1024, 1, 1, 32);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        byte[] destination = new byte[70];

        int written = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), destination, options, CancellationToken.None);

        Assert.Equal(70, written);
    }

    [Fact]
    public void Writable_stream_destination_does_not_reserve_owned_png_output()
    {
        SvgRenderLimits limits = new(131_072, 1024, 2, 4, 1024, 1, 1, 32);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        using MemoryStream destination = new(70);

        int written = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), destination, options, CancellationToken.None);

        Assert.Equal(70, written);
        Assert.Equal(70, destination.Length);
    }

    [Fact]
    public void Group_opacity_reserves_an_isolated_surface_before_allocation()
    {
        SvgRenderLimits limits = new(20_000, 1024, 64, 4_096, 1024, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g opacity='0.5'><rect width='64' height='64'/></g></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Async_cancellation_returns_a_canceled_task()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task<ReadOnlyMemory<byte>> task = SvgConverter.ConvertToPngAsync("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task Async_conversion_matches_the_synchronous_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> synchronous = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> asynchronous = await SvgConverter.ConvertToPngAsync(svg, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(synchronous.ToArray(), asynchronous.ToArray());
    }

    [Fact]
    public void Supports_non_array_backed_input_and_destination_memory()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using NonArrayMemory input = new(svg);
        using NonArrayMemory destination = new(new byte[expected.Length]);

        Assert.False(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)input.Memory, out _));
        Assert.False(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)destination.Memory, out _));
        int written = SvgConverter.ConvertToPng(input.Memory, destination.Memory, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination.Memory.Span[..written].ToArray());
    }

    [Fact]
    public async Task Concurrent_owned_conversions_return_independent_exact_results()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Task<ReadOnlyMemory<byte>>[] tasks = Enumerable.Range(0, 8).Select(_ => SvgConverter.ConvertToPngAsync(svg, SvgRenderOptions.Default, CancellationToken.None)).ToArray();

        ReadOnlyMemory<byte>[] results = await Task.WhenAll(tasks);

        foreach (ReadOnlyMemory<byte> result in results) Assert.Equal(expected, result.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(results[0], out ArraySegment<byte> first));
        Assert.True(MemoryMarshal.TryGetArray(results[1], out ArraySegment<byte> second));
        Assert.NotSame(first.Array, second.Array);
    }

    [Fact]
    public void Prohibited_doctype_is_reported_as_malformed_public_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Destination_conversion_writes_the_same_png_without_an_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];

        int written = SvgConverter.ConvertToPng(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, new byte[1], SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.InsufficientDestination, exception.ErrorCode);
    }

    [Fact]
    public void Stream_destination_writes_the_same_png_without_an_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using MemoryStream destination = new(expected.Length);

        int written = SvgConverter.ConvertToPng(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination.ToArray());
        using MemoryStream nonWritableDestination = new(new byte[expected.Length], false);
        Assert.Throws<ArgumentException>(() => SvgConverter.ConvertToPng(svg, nonWritableDestination, SvgRenderOptions.Default, CancellationToken.None));
    }

    [Fact]
    public async Task Async_destination_conversion_matches_sync_destination_conversion()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] synchronous = new byte[128];
        byte[] asynchronous = new byte[128];

        int syncLength = SvgConverter.ConvertToPng(svg, synchronous, SvgRenderOptions.Default, CancellationToken.None);
        int asyncLength = await SvgConverter.ConvertToPngAsync(svg, asynchronous, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(syncLength, asyncLength);
        Assert.Equal(synchronous.AsSpan(0, syncLength).ToArray(), asynchronous.AsSpan(0, asyncLength).ToArray());
    }

    [Fact]
    public async Task Async_stream_conversion_uses_asynchronous_destination_writes()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using AsyncWriteOnlyStream destination = new();

        int written = await SvgConverter.ConvertToPngAsync(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination.ToArray());
    }

    private sealed class AsyncWriteOnlyStream : Stream
    {
        private readonly MemoryStream _storage = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        internal byte[] ToArray() => _storage.ToArray();

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The async conversion must not use synchronous stream writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The async conversion must not use synchronous stream writes.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storage.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _storage.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NonArrayMemory : MemoryManager<byte>
    {
        private readonly byte[] _storage;

        internal NonArrayMemory(byte[] storage) => _storage = storage;

        public override Span<byte> GetSpan() => _storage;

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }
}
