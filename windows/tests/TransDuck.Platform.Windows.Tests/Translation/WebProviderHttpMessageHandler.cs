// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;

namespace TransDuck.Platform.Windows.Tests.Translation;

internal sealed class WebProviderHttpMessageHandler(
    Func<WebProviderCapturedRequest, int, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<WebProviderCapturedRequest> _requests = [];
    private readonly TaskCompletionSource<WebProviderCapturedRequest> _firstRequest = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<WebProviderCapturedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public Task<WebProviderCapturedRequest> WaitForFirstRequestAsync() => _firstRequest.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var captured = new WebProviderCapturedRequest(
            request.Method,
            request.RequestUri,
            CaptureHeaders(request),
            body);
        int requestNumber;
        lock (_gate)
        {
            requestNumber = _requests.Count;
            _requests.Add(captured);
        }

        _firstRequest.TrySetResult(captured);
        return await responseFactory(captured, requestNumber, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CaptureHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }
        }

        return headers;
    }
}

internal sealed record WebProviderCapturedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Body);
