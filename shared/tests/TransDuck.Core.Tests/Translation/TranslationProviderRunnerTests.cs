// Copyright (c) 2026 maywine. All rights reserved.

using System.Runtime.CompilerServices;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class TranslationProviderRunnerTests
{
    [Fact]
    public async Task RunAsync_CollectsIndependentStreamingResults()
    {
        var firstUpdates = new List<string>();
        var secondUpdates = new List<string>();
        var first = TranslationProviderRunner.RunAsync(
            new TestProvider("first", [TranslationStreamEvent.Delta("one"), TranslationStreamEvent.Completed()]),
            Request("first"),
            firstUpdates.Add,
            CancellationToken.None);
        var second = TranslationProviderRunner.RunAsync(
            new TestProvider("second", [TranslationStreamEvent.Delta("two"), TranslationStreamEvent.Completed()]),
            Request("second"),
            secondUpdates.Add,
            CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(TranslationStreamEventKind.Completed, result.TerminalKind));
        Assert.Equal("one", results[0].Text);
        Assert.Equal("two", results[1].Text);
        Assert.Equal(["one"], firstUpdates);
        Assert.Equal(["two"], secondUpdates);
    }

    [Fact]
    public async Task RunAsync_MapsMissingTerminalEmptyCompletionAndCancellation()
    {
        var missingTerminal = await TranslationProviderRunner.RunAsync(
            new TestProvider("missing", [TranslationStreamEvent.Delta("partial")]),
            Request("missing"),
            null,
            CancellationToken.None);
        var empty = await TranslationProviderRunner.RunAsync(
            new TestProvider("empty", [TranslationStreamEvent.Completed()]),
            Request("empty"),
            null,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await TranslationProviderRunner.RunAsync(
            new TestProvider("cancelled", [], waitForCancellation: true),
            Request("cancelled"),
            null,
            cancellation.Token);

        Assert.Equal(QueryErrorCode.ProviderUnavailable, missingTerminal.ErrorCode);
        Assert.True(missingTerminal.Retryable);
        Assert.Equal(QueryErrorCode.Internal, empty.ErrorCode);
        Assert.False(empty.Retryable);
        Assert.Equal(TranslationStreamEventKind.Cancelled, cancelled.TerminalKind);
    }

    [Fact]
    public async Task RunAsync_ConcurrentRunsStartTogetherAndIsolateOneProviderFailure()
    {
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task WaitForBothAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                allStarted.TrySetResult();
            }

            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var successful = TranslationProviderRunner.RunAsync(
            new TestProvider(
                "successful",
                [TranslationStreamEvent.Delta("kept"), TranslationStreamEvent.Completed()],
                beforeEvents: WaitForBothAsync),
            Request("successful"),
            null,
            CancellationToken.None);
        var failed = TranslationProviderRunner.RunAsync(
            new TestProvider(
                "failed",
                [TranslationStreamEvent.Failed("safe", QueryErrorCode.Network, retryable: true)],
                beforeEvents: WaitForBothAsync),
            Request("failed"),
            null,
            CancellationToken.None);

        var results = await Task.WhenAll(successful, failed);

        Assert.Equal(2, started);
        Assert.Equal(TranslationStreamEventKind.Completed, results[0].TerminalKind);
        Assert.Equal("kept", results[0].Text);
        Assert.Equal(TranslationStreamEventKind.Failed, results[1].TerminalKind);
        Assert.Equal(QueryErrorCode.Network, results[1].ErrorCode);
        Assert.True(results[1].Retryable);
    }

    private static TranslationProviderRequest Request(string providerId) => new(
        new ProviderDescriptor(providerId),
        new Uri("https://provider.example.test/translate"),
        "model",
        "query",
        "en-US",
        "zh-Hans",
        new TranslationCredentials(null),
        TimeSpan.FromSeconds(1));

    private sealed class TestProvider(
        string providerId,
        IReadOnlyList<TranslationStreamEvent> events,
        bool waitForCancellation = false,
        Func<CancellationToken, Task>? beforeEvents = null) : ITranslationProvider
    {
        public ProviderRegistration Registration { get; } = new(
            new ProviderDescriptor(providerId),
            ProviderCapability.Translation | ProviderCapability.Streaming);

        public async IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
            TranslationProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (beforeEvents is not null)
            {
                await beforeEvents(cancellationToken);
            }

            foreach (var item in events)
            {
                yield return item;
            }
        }
    }
}
