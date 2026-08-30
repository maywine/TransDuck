// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;

namespace TransDuck.Infrastructure.Tests.Translation;

internal sealed class OversizedResponseContent : HttpContent
{
    public OversizedResponseContent(long declaredLength)
    {
        Headers.ContentLength = declaredLength;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = Headers.ContentLength ?? 0;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        Task.FromException(new InvalidOperationException("The bounded DeepL reader must reject this content before reading it."));

    protected override Task<Stream> CreateContentReadStreamAsync() =>
        Task.FromException<Stream>(new InvalidOperationException("The bounded DeepL reader must not open this content."));
}
