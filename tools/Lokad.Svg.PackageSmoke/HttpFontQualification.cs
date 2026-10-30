using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg;
using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;

internal static class HttpFontQualification
{
    internal static async Task RunAsync()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "Primary.ttf"));
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "smoke",
            files = new[]
            {
                new { relativePath = "Primary.ttf", byteSize = font.Length, sha256 = Convert.ToHexString(SHA256.HashData(font)) },
                new { relativePath = "Unused.ttf", byteSize = 1, sha256 = new string('0', 64) }
            },
            families = new[]
            {
                new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "Primary.ttf" },
                new { requestedFamily = "Unused", resolvedFamily = "Unused", relativeFontFile = "Unused.ttf" }
            }
        });
        byte[] text = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><text x='2' y='24' font-family='Example' font-size='20'>A</text></svg>"u8.ToArray();
        byte[] shape = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8'/></svg>"u8.ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, "smoke");
        using SvgFontSnapshot explicitFonts = await SvgFontSnapshot.CreateAsync(text, catalog, new Programs(font), CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(text,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, explicitFonts), CancellationToken.None).ToArray();
        await CachedLifecycleAsync(manifest, font, text, shape, expected);
        await AcquisitionRefusalAsync(manifest, font, text);
        await ShutdownDuringAcquisitionAsync(manifest, font, text);
        Console.WriteLine("Installed HTTP font selection, concurrent reuse, cancellation, shutdown borrowers, async output and admission refusal passed.");

        static async Task CachedLifecycleAsync(byte[] manifest, byte[] font, byte[] text, byte[] shape, byte[] expected)
        {
            TaskCompletionSource manifestReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource programReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new HttpFontSmokeHandler(manifest, font) { ManifestGate = manifestReady.Task, ProgramGate = programReady.Task };
            using var client = new HttpClient(handler);
            using var root = new TestMemoryBudget(8_000_000);
            using IMemoryScope cacheOwner = root.CreateScope();
            using IMemoryScope operationOwner = root.CreateScope();
            SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(operationOwner);
            await using var resolver = SvgHttpFontPackResolver.Create("smoke", new("https://fonts.invalid/smoke/"), client,
                new(TimeSpan.FromSeconds(10), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000), cacheOwner);
            if (handler.Requests != 0) throw new InvalidOperationException("Resolver construction performed font I/O.");
            using (SvgPreparedDocument free = await SvgPreparedDocument.CreateAsync(shape, resolver, options, CancellationToken.None))
            using (SvgPngResult image = await free.ConvertToOwnedPngAsync(CancellationToken.None))
                if (handler.Requests != 0 || image.Memory.IsEmpty) throw new InvalidOperationException("Cold font-free preparation acquired fonts.");

            using var canceled = new CancellationTokenSource();
            Task<SvgPreparedDocument> first = SvgPreparedDocument.CreateAsync(text, resolver, options, canceled.Token).AsTask();
            await handler.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<SvgPreparedDocument> survivor = SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None).AsTask();
            canceled.Cancel();
            await ExpectCancellation(first, canceled.Token);
            using (SvgPreparedDocument free = await SvgPreparedDocument.CreateAsync(shape, resolver, options, CancellationToken.None))
            using (SvgPngResult image = await free.ConvertToOwnedPngAsync(CancellationToken.None))
                if (handler.Requests != 1 || image.Memory.IsEmpty || survivor.IsCompleted) throw new InvalidOperationException("Font-free work waited for shared acquisition.");
            manifestReady.TrySetResult();
            await handler.ProgramRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            programReady.TrySetResult();
            using (SvgPreparedDocument document = await survivor)
            using (SvgPngResult result = await document.ConvertToOwnedPngAsync(CancellationToken.None))
                if (!result.Memory.Span.SequenceEqual(expected)) throw new InvalidOperationException("HTTP and explicit-provider pixels differ.");
            if (handler.Requests != 2) throw new InvalidOperationException("Font acquisition was not selective/coalesced.");

            // Each output form appears once because its allocation/ownership path differs.
            using (SvgPreparedDocument memory = await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None))
            {
                byte[] destination = Enumerable.Repeat((byte)0xA5, expected.Length + 7).ToArray();
                int length = memory.ConvertToPng(destination, CancellationToken.None);
                if (length != expected.Length || !destination.AsSpan(0, length).SequenceEqual(expected) || destination.Skip(length).Any(value => value != 0xA5))
                    throw new InvalidOperationException("Installed caller-memory output modified its destination contract.");
            }
            using (SvgPreparedDocument streamed = await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None))
            using (var destination = new DelayedPngStream())
            {
                Task<int> conversion = streamed.ConvertToPngAsync(destination, CancellationToken.None);
                await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (conversion.IsCompleted || root.Reserved <= 0) throw new InvalidOperationException("Async stream output ignored backpressure or released scratch.");
                destination.Finish.TrySetResult();
                if (await conversion != expected.Length || !destination.Digest().AsSpan().SequenceEqual(SHA256.HashData(expected)) || !destination.CanWrite)
                    throw new InvalidOperationException("Installed async stream output lost bytes or caller ownership.");
            }
            long cached = root.Reserved;
            using (SvgPreparedDocument streamed = await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None))
            using (var destination = new DelayedPngStream())
            using (var streamCancellation = new CancellationTokenSource())
            {
                Task<int> conversion = streamed.ConvertToPngAsync(destination, streamCancellation.Token);
                await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (root.Reserved <= cached) throw new InvalidOperationException("Blocked async conversion released its working storage.");
                streamCancellation.Cancel();
                await ExpectCancellation(conversion, streamCancellation.Token);
                if (root.Reserved != cached || !destination.CanWrite) throw new InvalidOperationException("Async cancellation leaked storage or disposed the destination.");
            }
            using (SvgPreparedDocument refused = await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None))
            {
                root.Limit = root.Reserved;
                await ExpectRefusal(refused.ConvertToOwnedPngAsync(CancellationToken.None));
                root.Limit = 8_000_000;
            }
            if (root.Reserved != cached || handler.Requests != 2) throw new InvalidOperationException("Rendering refusal leaked storage or reacquired fonts.");

            SvgPreparedDocument[] borrowers = await Task.WhenAll(
                Task.Run(async () => await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None)),
                Task.Run(async () => await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None)));
            using SvgPreparedDocument unused = borrowers[0];
            using SvgPreparedDocument retained = borrowers[1];
            if (handler.Requests != 2) throw new InvalidOperationException("Concurrent cached preparation downloaded programs again.");
            await resolver.DisposeAsync();
            if (root.Reserved <= font.Length) throw new InvalidOperationException("Shutdown refunded live document/font borrowers.");
            unused.Dispose();
            using SvgPngResult output = await retained.ConvertToOwnedPngAsync(CancellationToken.None);
            retained.Dispose();
            if (root.Reserved != output.Memory.Length || !output.Memory.Span.SequenceEqual(expected)) throw new InvalidOperationException("Shutdown borrowers lost rendering or output ownership.");
            using (var destination = new DelayedPngStream())
            {
                Task write = destination.WriteAsync(output.Memory, CancellationToken.None).AsTask();
                await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (write.IsCompleted || root.Reserved != output.Memory.Length) throw new InvalidOperationException("A delayed write outlived its charged PNG.");
                destination.Finish.TrySetResult();
                await write;
                if (!destination.Digest().AsSpan().SequenceEqual(SHA256.HashData(expected))) throw new InvalidOperationException("Delayed output bytes changed.");
            }
            using (var destination = new DelayedPngStream())
            using (var writeCancellation = new CancellationTokenSource())
            {
                Task write = destination.WriteAsync(output.Memory, writeCancellation.Token).AsTask();
                await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                writeCancellation.Cancel();
                await ExpectCancellation(write, writeCancellation.Token);
                if (root.Reserved != output.Memory.Length) throw new InvalidOperationException("Canceled publication refunded caller-owned output.");
            }
            output.Dispose();
            if (root.Reserved != 0 || root.Disposed) throw new InvalidOperationException("Installed lifecycle cleanup lost or disposed the shared allowance.");
            using var response = await client.GetAsync("https://fonts.invalid/smoke/manifest.json", CancellationToken.None);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Resolver shutdown disposed the borrowed client.");
        }

        static async Task AcquisitionRefusalAsync(byte[] manifest, byte[] font, byte[] text)
        {
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new HttpFontSmokeHandler(manifest, font) { ManifestGate = ready.Task };
            using var client = new HttpClient(handler);
            using var root = new TestMemoryBudget(8_000_000);
            using IMemoryScope cacheOwner = root.CreateScope();
            using IMemoryScope operationOwner = root.CreateScope();
            await using var resolver = SvgHttpFontPackResolver.Create("smoke", new("https://fonts.invalid/smoke/"), client,
                new(TimeSpan.FromSeconds(10), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000), cacheOwner);
            SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(operationOwner);
            Task<SvgPreparedDocument> refused = SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None).AsTask();
            await handler.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            root.Limit = root.Reserved;
            ready.TrySetResult();
            await ExpectRefusal(refused);
            if (handler.Requests != 1 || root.Reserved != 0) throw new InvalidOperationException("Acquisition refusal retried or leaked its owners.");
            root.Limit = 8_000_000;
            using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(text, resolver, options, CancellationToken.None);
            using SvgPngResult image = await retry.ConvertToOwnedPngAsync(CancellationToken.None);
            if (handler.Requests != 3 || image.Memory.IsEmpty) throw new InvalidOperationException("A later operation could not recover from acquisition refusal.");
            await resolver.DisposeAsync();
            image.Dispose();
            if (root.Reserved != 0) throw new InvalidOperationException("Recovery retained completed acquisition/output storage.");
        }

        static async Task ExpectCancellation(Task task, CancellationToken token)
        {
            try { await task; }
            catch (OperationCanceledException exception) when (exception.CancellationToken == token) { return; }
            throw new InvalidOperationException("The installed operation lost caller cancellation.");
        }

        static async Task ExpectRefusal(Task task)
        {
            try { await task; }
            catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.LimitExceeded) { return; }
            throw new InvalidOperationException("The installed operation lost structured admission refusal.");
        }

        static async Task ShutdownDuringAcquisitionAsync(byte[] manifest, byte[] font, byte[] text)
        {
            TaskCompletionSource response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new HttpFontSmokeHandler(manifest, font) { ManifestGate = response.Task, CancellationCleanupGate = cleanup.Task };
            using var client = new HttpClient(handler);
            using var root = new TestMemoryBudget(8_000_000);
            await using var resolver = SvgHttpFontPackResolver.Create("smoke", new("https://fonts.invalid/smoke/"), client,
                new(TimeSpan.FromSeconds(10), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000), root);
            Task<SvgPreparedDocument> pending = SvgPreparedDocument.CreateAsync(text, resolver, SvgRenderOptions.Default, CancellationToken.None).AsTask();
            await handler.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task shutdown = resolver.DisposeAsync().AsTask();
            bool stopped = false;
            try
            {
                await handler.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (shutdown.IsCompleted || root.Reserved == 0) throw new InvalidOperationException("Shutdown did not retain/await active acquisition cleanup.");
            }
            finally
            {
                cleanup.TrySetResult();
                response.TrySetResult();
                await shutdown;
                try { using SvgPreparedDocument unexpected = await pending; }
                catch (OperationCanceledException) { stopped = true; }
            }
            if (!stopped || handler.Requests != 1 || root.Reserved != 0 || root.Disposed)
                throw new InvalidOperationException("Acquisition shutdown lost cancellation, cleanup or the borrowed root.");
        }
    }

    private sealed class Programs(ReadOnlyMemory<byte> font) : ISvgFontProgramSourceProvider
    {
        /// <summary>Loans immutable caller-held fixture bytes through snapshot disposal; concurrent reads honor cancellation.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (file.RelativePath != "Primary.ttf") throw new InvalidOperationException("The explicit provider selected an unused face.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, font));
        }
    }
}
