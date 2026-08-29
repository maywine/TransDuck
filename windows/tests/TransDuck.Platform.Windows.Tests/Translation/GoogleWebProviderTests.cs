// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

public sealed class GoogleWebProviderTests
{
    private const string CredentialCanary = "GOOGLE_CREDENTIAL_CANARY";
    private const string QueryCanary = "QUERY_CANARY_GOOGLE_WEB";

    [Fact]
    public async Task TranslateAsync_UsesExactGetQueryMapsLanguagesAndConcatenatesSentencesWithoutCredentials()
    {
        using var handler = new WebProviderHttpMessageHandler(static (request, requestNumber, _) =>
        {
            Assert.Equal(0, requestNumber);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                """{"sentences":[{"trans":"first "},{"trans":"second"}]}"""));
        });
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new GoogleWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            Request(sourceLanguage: "zh-Hans", targetLanguage: "zh-Hant",
                credentials: new TranslationCredentials(CredentialCanary)));
        var captured = Assert.Single(handler.Requests);

        Assert.Equal(GoogleWebProvider.DefaultEndpoint, "https://translate.google.com/translate_a/single");
        Assert.Equal(TranslationProviderIds.Google, provider.Registration.Provider.ProviderId);
        Assert.Equal(ProviderCapability.Translation, provider.Registration.Capabilities);
        Assert.Equal("first second", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal("translate.google.com", captured.RequestUri!.Host);
        Assert.Equal("/translate_a/single", captured.RequestUri.AbsolutePath);
        Assert.Equal(
            new[]
            {
                "client=gtx",
                "dj=1",
                "dt=t",
                "ie=UTF-8",
                "sl=zh-CN",
                "tl=zh-TW",
                "q=" + QueryCanary,
            },
            ParseQuery(captured.RequestUri).Select(pair => pair.Key + "=" + pair.Value));
        Assert.Null(captured.Body);
        Assert.False(captured.Headers.ContainsKey("Authorization"));
        Assert.All(captured.Headers.SelectMany(header => header.Value), value =>
            Assert.DoesNotContain(CredentialCanary, value, StringComparison.Ordinal));
        Assert.DoesNotContain(CredentialCanary, captured.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_DoesNotAccessTranslationCredentials()
    {
        var source = StripComments(File.ReadAllText(FindProviderPath("GoogleWebProvider.cs")));

        Assert.DoesNotContain(".Credentials", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetApiKey", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, QueryErrorCode.InvalidRequest, false)]
    [InlineData(429, QueryErrorCode.RateLimited, true)]
    [InlineData(503, QueryErrorCode.ProviderUnavailable, true)]
    public async Task TranslateAsync_MapsHttpStatusesWithoutLeakingCredentialOrQuery(
        int statusCode,
        QueryErrorCode expectedCode,
        bool retryable)
    {
        using var handler = new WebProviderHttpMessageHandler((_, _, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response((HttpStatusCode)statusCode, "UPSTREAM_BODY_CANARY")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new GoogleWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            Request(credentials: new TranslationCredentials(CredentialCanary)));

        AssertSafeFailure(events, expectedCode, retryable, CredentialCanary, QueryCanary, "UPSTREAM_BODY_CANARY");
    }

    [Fact]
    public async Task TranslateAsync_RejectsMalformedAndOversizedResponsesWithoutLeakingCredentialOrQuery()
    {
        using var malformedHandler = new WebProviderHttpMessageHandler((_, _, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """{"sentences":[{"trans":false}]}""")));
        using var malformedClient = ProviderTranslationTestSupport.CreateHttpClient(malformedHandler);
        var malformedProvider = new GoogleWebProvider(malformedClient);

        var malformed = await ProviderTranslationTestSupport.ReadEventsAsync(
            malformedProvider,
            Request(credentials: new TranslationCredentials(CredentialCanary)));

        AssertSafeFailure(malformed, QueryErrorCode.Internal, retryable: false, CredentialCanary, QueryCanary);

        using var oversizedHandler = new WebProviderHttpMessageHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedResponseContent((1024 * 1024) + 1),
            }));
        using var oversizedClient = ProviderTranslationTestSupport.CreateHttpClient(oversizedHandler);
        var oversizedProvider = new GoogleWebProvider(oversizedClient);

        var oversized = await ProviderTranslationTestSupport.ReadEventsAsync(
            oversizedProvider,
            Request(credentials: new TranslationCredentials(CredentialCanary)));

        AssertSafeFailure(oversized, QueryErrorCode.Internal, retryable: false, CredentialCanary, QueryCanary);
    }

    [Fact]
    public async Task TranslateAsync_DistinguishesCallerCancellationAndTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var cancellationHandler = new WebProviderHttpMessageHandler(static (_, _, token) =>
            ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var cancellationClient = ProviderTranslationTestSupport.CreateHttpClient(cancellationHandler);
        var cancellationProvider = new GoogleWebProvider(cancellationClient);
        var cancellationTask = ProviderTranslationTestSupport.ReadEventsAsync(
            cancellationProvider,
            Request(credentials: new TranslationCredentials(CredentialCanary)),
            cancellation.Token);

        await cancellationHandler.WaitForFirstRequestAsync();
        cancellation.Cancel();
        var cancelled = await cancellationTask;

        Assert.Equal(TranslationStreamEventKind.Cancelled, Assert.Single(cancelled).Kind);

        using var timeoutHandler = new WebProviderHttpMessageHandler(static (_, _, token) =>
            ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var timeoutClient = ProviderTranslationTestSupport.CreateHttpClient(timeoutHandler);
        var timeoutProvider = new GoogleWebProvider(timeoutClient);

        var timedOut = await ProviderTranslationTestSupport.ReadEventsAsync(
            timeoutProvider,
            Request(credentials: new TranslationCredentials(CredentialCanary), timeout: TimeSpan.FromMilliseconds(150)));

        AssertSafeFailure(timedOut, QueryErrorCode.Timeout, retryable: true, CredentialCanary, QueryCanary);
    }

    private static TranslationProviderRequest Request(
        string? sourceLanguage = "en-US",
        string targetLanguage = "zh-Hans",
        TranslationCredentials? credentials = null,
        TimeSpan? timeout = null) => new(
        new ProviderDescriptor(TranslationProviderIds.Google),
        new Uri(GoogleWebProvider.DefaultEndpoint),
        Model: null,
        Text: QueryCanary,
        SourceLanguage: sourceLanguage,
        TargetLanguage: targetLanguage,
        Credentials: credentials ?? new TranslationCredentials(null),
        Timeout: timeout ?? TimeSpan.FromSeconds(2));

    private static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(Uri uri) => uri.Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(item =>
        {
            var separator = item.IndexOf('=');
            return separator < 0
                ? new KeyValuePair<string, string>(Uri.UnescapeDataString(item), string.Empty)
                : new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(item[..separator]),
                    Uri.UnescapeDataString(item[(separator + 1)..]));
        })
        .ToArray();

    private static void AssertSafeFailure(
        IReadOnlyList<TranslationStreamEvent> events,
        QueryErrorCode expectedCode,
        bool retryable,
        params string[] forbiddenValues)
    {
        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(expectedCode, failure.ErrorCode);
        Assert.Equal(retryable, failure.Retryable);
        Assert.All(forbiddenValues, value =>
            Assert.DoesNotContain(value, failure.ErrorMessage!, StringComparison.Ordinal));
    }

    private static string FindProviderPath(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "windows",
                    "src",
                    "TransDuck.Platform.Windows",
                    "Translation",
                    fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("The requested web provider source file was not found from the test host path.");
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }
}
