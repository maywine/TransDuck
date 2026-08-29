// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;
using System.Net.Http.Headers;

namespace TransDuck.Platform.Windows.Tests.Translation;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _expectedApiKey;
    private readonly Func<CapturedHttpRequest, CancellationToken, Task<HttpResponseMessage>> _responseFactory;
    private readonly TaskCompletionSource<CapturedHttpRequest> _requestReceived = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeHttpMessageHandler(
        string expectedApiKey,
        Func<CapturedHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        _expectedApiKey = expectedApiKey;
        _responseFactory = responseFactory;
    }

    public Task<CapturedHttpRequest> WaitForRequestAsync() => _requestReceived.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var authorization = request.Headers.Authorization;
        var captured = new CapturedHttpRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Accept.Any(static header =>
                string.Equals(header.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)),
            authorization is not null,
            HasExpectedBearerAuthorization(authorization),
            body);
        _requestReceived.TrySetResult(captured);
        return await _responseFactory(captured, cancellationToken).ConfigureAwait(false);
    }

    private bool HasExpectedBearerAuthorization(AuthenticationHeaderValue? authorization) =>
        authorization is { Scheme: "Bearer", Parameter: { } parameter } &&
        string.Equals(parameter, _expectedApiKey, StringComparison.Ordinal);
}

internal sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    bool AcceptsServerSentEvents,
    bool HasAuthorization,
    bool HasExpectedBearerAuthorization,
    string? Body);
