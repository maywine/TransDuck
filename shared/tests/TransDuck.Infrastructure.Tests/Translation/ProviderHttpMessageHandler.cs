// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;
using System.Net.Http.Headers;

namespace TransDuck.Infrastructure.Tests.Translation;

internal sealed class ProviderHttpMessageHandler : HttpMessageHandler
{
    private readonly string? _expectedApiKey;
    private readonly string? _expectedAuthorizationScheme;
    private readonly Func<ProviderCapturedRequest, CancellationToken, Task<HttpResponseMessage>> _responseFactory;
    private readonly TaskCompletionSource<ProviderCapturedRequest> _requestReceived = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public ProviderHttpMessageHandler(
        string? expectedApiKey,
        string? expectedAuthorizationScheme,
        Func<ProviderCapturedRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        _expectedApiKey = expectedApiKey;
        _expectedAuthorizationScheme = expectedAuthorizationScheme;
        _responseFactory = responseFactory;
    }

    public Task<ProviderCapturedRequest> WaitForRequestAsync() => _requestReceived.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var authorization = request.Headers.Authorization;
        var captured = new ProviderCapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Accept.Select(header => header.MediaType ?? string.Empty).ToArray(),
            authorization is not null,
            authorization?.Scheme,
            HasExpectedAuthorization(authorization),
            body);
        _requestReceived.TrySetResult(captured);
        return await _responseFactory(captured, cancellationToken).ConfigureAwait(false);
    }

    private bool HasExpectedAuthorization(AuthenticationHeaderValue? authorization) =>
        authorization is { Scheme: { } scheme, Parameter: { } parameter } &&
        string.Equals(scheme, _expectedAuthorizationScheme, StringComparison.Ordinal) &&
        string.Equals(parameter, _expectedApiKey, StringComparison.Ordinal);
}

internal sealed record ProviderCapturedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyList<string> AcceptMediaTypes,
    bool HasAuthorization,
    string? AuthorizationScheme,
    bool HasExpectedAuthorization,
    string? Body);
