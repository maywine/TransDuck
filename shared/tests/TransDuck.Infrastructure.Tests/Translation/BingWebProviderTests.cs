// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Translation;

namespace TransDuck.Infrastructure.Tests.Translation;

public sealed class BingWebProviderTests
{
    private const string CookieCanary = "BING_COOKIE_CANARY";
    private const string QueryCanary = "QUERY_CANARY_BING_WEB";
    private const string FirstIg = "IG_FIRST";
    private const string FirstIid = "IID_FIRST";
    private const string FirstKey = "123456";
    private const string FirstToken = "TOKEN_FIRST_CANARY";
    private const string SecondIg = "IG_SECOND";
    private const string SecondIid = "IID_SECOND";
    private const string SecondKey = "654321";
    private const string SecondToken = "TOKEN_SECOND_CANARY";

    [Fact]
    public async Task TranslateAsync_FetchesTokenThenPostsExactBingFormWithOptionalCookie()
    {
        using var handler = new WebProviderHttpMessageHandler((request, requestNumber, _) => requestNumber switch
        {
            0 => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken))),
            1 => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                """[{"translations":[{"text":"bing translated"}]}]""")),
            _ => throw new InvalidOperationException("Bing must issue exactly one profile GET and one translation POST."),
        });
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new BingWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());
        var requests = handler.Requests;

        Assert.Equal(BingWebProvider.DefaultEndpoint, "https://cn.bing.com/translator");
        Assert.Equal(TranslationProviderIds.Bing, provider.Registration.Provider.ProviderId);
        Assert.Equal(ProviderCapability.Translation, provider.Registration.Capabilities);
        Assert.Equal("bing translated", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(2, requests.Count);
        AssertProfileRequest(requests[0], CookieCanary);
        AssertTranslationRequest(
            requests[1],
            CookieCanary,
            FirstIg,
            FirstIid,
            FirstKey,
            FirstToken,
            expectedSourceLanguage: "auto-detect",
            expectedTargetLanguage: "zh-Hans");
    }

    [Fact]
    public async Task TranslateAsync_AllowsMissingCookieWithoutSendingCookieHeader()
    {
        using var handler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(
            requestNumber == 0
                ? ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                    TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken))
                : ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                    """[{"translations":[{"text":"anonymous"}]}]""")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new BingWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            Request(credentials: new TranslationCredentials(null)));

        Assert.Equal("anonymous", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.False(request.Headers.ContainsKey("Cookie")));
    }

    [Fact]
    public async Task TranslateAsync_RejectsCrLfAndOversizedCookiesBeforeSendingAndDoesNotLeakThem()
    {
        var invalidCookies = new[]
        {
            CookieCanary + "\r\nInjected: header",
            CookieCanary + new string('x', 8193),
        };

        foreach (var cookie in invalidCookies)
        {
            using var handler = new WebProviderHttpMessageHandler(static (_, _, _) =>
                throw new InvalidOperationException("Invalid cookies must not reach HTTP."));
            using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
            var provider = new BingWebProvider(httpClient);

            var events = await ProviderTranslationTestSupport.ReadEventsAsync(
                provider,
                Request(credentials: new TranslationCredentials(cookie)));

            AssertSafeFailure(events, QueryErrorCode.InvalidRequest, retryable: false, cookie, QueryCanary);
            Assert.Empty(handler.Requests);
        }
    }

    [Fact]
    public async Task TranslateAsync_RefreshesTokenOnceAfter205PayloadAndUsesTheNewToken()
    {
        using var handler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(requestNumber switch
        {
            0 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken)),
            1 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """{"statusCode":205}"""),
            2 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                TokenHtml(SecondIg, SecondIid, SecondKey, SecondToken)),
            3 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                """[{"translations":[{"text":"refreshed"}]}]"""),
            _ => throw new InvalidOperationException("205 refresh may make only one additional profile request."),
        }));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new BingWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());
        var requests = handler.Requests;

        Assert.Equal("refreshed", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(4, requests.Count);
        AssertProfileRequest(requests[0], CookieCanary);
        AssertTranslationRequest(requests[1], CookieCanary, FirstIg, FirstIid, FirstKey, FirstToken,
            "auto-detect", "zh-Hans");
        AssertProfileRequest(requests[2], CookieCanary);
        AssertTranslationRequest(requests[3], CookieCanary, SecondIg, SecondIid, SecondKey, SecondToken,
            "auto-detect", "zh-Hans");
    }

    [Fact]
    public async Task TranslateAsync_DoesNotRefreshMoreThanOnceAfterSecond205Status()
    {
        using var handler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(requestNumber switch
        {
            0 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken)),
            1 => ProviderTranslationTestSupport.Response(HttpStatusCode.ResetContent, "{}"),
            2 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                TokenHtml(SecondIg, SecondIid, SecondKey, SecondToken)),
            3 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """{"statusCode":205}"""),
            _ => throw new InvalidOperationException("A second 205 must not trigger a third profile fetch."),
        }));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new BingWebProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());

        AssertSafeFailure(events, QueryErrorCode.ProviderUnavailable, retryable: true,
            CookieCanary, FirstToken, SecondToken, QueryCanary);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task TranslateAsync_Maps429AndMalformedOrOversizedResponsesWithoutSensitiveValues()
    {
        using var rateLimitHandler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(
            requestNumber == 0
                ? ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                    TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken))
                : ProviderTranslationTestSupport.Response((HttpStatusCode)429, "UPSTREAM_BODY_CANARY")));
        using var rateLimitClient = ProviderTranslationTestSupport.CreateHttpClient(rateLimitHandler);
        var rateLimitProvider = new BingWebProvider(rateLimitClient);

        var rateLimited = await ProviderTranslationTestSupport.ReadEventsAsync(rateLimitProvider, Request());

        AssertSafeFailure(rateLimited, QueryErrorCode.RateLimited, retryable: true,
            CookieCanary, FirstToken, QueryCanary, "UPSTREAM_BODY_CANARY");

        using var malformedHandler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(
            requestNumber == 0
                ? ProviderTranslationTestSupport.Response(HttpStatusCode.OK,
                    TokenHtml(FirstIg, FirstIid, FirstKey, FirstToken))
                : ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """[{"translations":[{}]}]""")));
        using var malformedClient = ProviderTranslationTestSupport.CreateHttpClient(malformedHandler);
        var malformedProvider = new BingWebProvider(malformedClient);

        var malformed = await ProviderTranslationTestSupport.ReadEventsAsync(malformedProvider, Request());

        AssertSafeFailure(malformed, QueryErrorCode.Internal, retryable: false,
            CookieCanary, FirstToken, QueryCanary);

        using var oversizedHandler = new WebProviderHttpMessageHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedResponseContent((1024 * 1024) + 1),
            }));
        using var oversizedClient = ProviderTranslationTestSupport.CreateHttpClient(oversizedHandler);
        var oversizedProvider = new BingWebProvider(oversizedClient);

        var oversized = await ProviderTranslationTestSupport.ReadEventsAsync(oversizedProvider, Request());

        AssertSafeFailure(oversized, QueryErrorCode.Internal, retryable: false, CookieCanary, QueryCanary);
        Assert.Single(oversizedHandler.Requests);
    }

    [Fact]
    public async Task TranslateAsync_DistinguishesCallerCancellationAndTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var cancellationHandler = new WebProviderHttpMessageHandler(static (_, _, token) =>
            ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var cancellationClient = ProviderTranslationTestSupport.CreateHttpClient(cancellationHandler);
        var cancellationProvider = new BingWebProvider(cancellationClient);
        var cancellationTask = ProviderTranslationTestSupport.ReadEventsAsync(
            cancellationProvider,
            Request(),
            cancellation.Token);

        await cancellationHandler.WaitForFirstRequestAsync();
        cancellation.Cancel();
        var cancelled = await cancellationTask;

        Assert.Equal(TranslationStreamEventKind.Cancelled, Assert.Single(cancelled).Kind);

        using var timeoutHandler = new WebProviderHttpMessageHandler(static (_, _, token) =>
            ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var timeoutClient = ProviderTranslationTestSupport.CreateHttpClient(timeoutHandler);
        var timeoutProvider = new BingWebProvider(timeoutClient);

        var timedOut = await ProviderTranslationTestSupport.ReadEventsAsync(
            timeoutProvider,
            Request(timeout: TimeSpan.FromMilliseconds(150)));

        AssertSafeFailure(timedOut, QueryErrorCode.Timeout, retryable: true, CookieCanary, QueryCanary);
    }

    private static TranslationProviderRequest Request(
        TranslationCredentials? credentials = null,
        TimeSpan? timeout = null) => new(
        new ProviderDescriptor(TranslationProviderIds.Bing),
        new Uri(BingWebProvider.DefaultEndpoint),
        Model: null,
        Text: QueryCanary,
        SourceLanguage: "auto",
        TargetLanguage: "zh-Hans",
        Credentials: credentials ?? new TranslationCredentials(CookieCanary),
        Timeout: timeout ?? TimeSpan.FromSeconds(2));

    private static string TokenHtml(string ig, string iid, string key, string token) =>
        $$"""
        <html>IG: "{{ig}}" <div data-iid="{{iid}}"></div>
        <script>var params_AbusePreventionHelper = [{{key}}, "{{token}}", 3600000];</script>
        </html>
        """;

    private static void AssertProfileRequest(WebProviderCapturedRequest request, string? expectedCookie)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("cn.bing.com", request.RequestUri!.Host);
        Assert.Equal("/translator", request.RequestUri.AbsolutePath);
        Assert.Empty(ParseQuery(request.RequestUri));
        Assert.Null(request.Body);
        AssertCookie(request, expectedCookie);
    }

    private static void AssertTranslationRequest(
        WebProviderCapturedRequest request,
        string? expectedCookie,
        string expectedIg,
        string expectedIid,
        string expectedKey,
        string expectedToken,
        string expectedSourceLanguage,
        string expectedTargetLanguage)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("cn.bing.com", request.RequestUri!.Host);
        Assert.Equal("/ttranslatev3", request.RequestUri.AbsolutePath);
        Assert.Equal(
            new[] { "isVertical=1", "IG=" + expectedIg, "IID=" + expectedIid },
            ParseQuery(request.RequestUri).Select(pair => pair.Key + "=" + pair.Value));
        Assert.Equal(
            new[]
            {
                "text=" + QueryCanary,
                "to=" + expectedTargetLanguage,
                "fromLang=" + expectedSourceLanguage,
                "token=" + expectedToken,
                "key=" + expectedKey,
            },
            ParseForm(request.Body!).Select(pair => pair.Key + "=" + pair.Value));
        AssertCookie(request, expectedCookie);
    }

    private static void AssertCookie(WebProviderCapturedRequest request, string? expectedCookie)
    {
        if (expectedCookie is null)
        {
            Assert.False(request.Headers.ContainsKey("Cookie"));
            return;
        }

        Assert.Equal(new[] { expectedCookie }, request.Headers["Cookie"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(Uri uri) => ParseEncodedPairs(
        uri.Query.TrimStart('?'));

    private static IReadOnlyList<KeyValuePair<string, string>> ParseForm(string form) => ParseEncodedPairs(form);

    private static IReadOnlyList<KeyValuePair<string, string>> ParseEncodedPairs(string encoded) => encoded
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(item =>
        {
            var separator = item.IndexOf('=');
            return separator < 0
                ? new KeyValuePair<string, string>(Uri.UnescapeDataString(item), string.Empty)
                : new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(item[..separator].Replace('+', ' ')),
                    Uri.UnescapeDataString(item[(separator + 1)..].Replace('+', ' ')));
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
}
