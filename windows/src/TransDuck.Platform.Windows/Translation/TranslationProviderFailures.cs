// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Net;
using System.Net.Http;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Maps transport and HTTP failures to fixed provider-neutral events without exposing upstream text.
/// </summary>
internal static class TranslationProviderFailures
{
    public static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    public static TranslationStreamEvent InvalidRequest() =>
        TranslationStreamEvent.Failed("翻译请求无效。", QueryErrorCode.InvalidRequest, retryable: false);

    public static TranslationStreamEvent Authentication() =>
        TranslationStreamEvent.Failed("翻译服务认证失败。", QueryErrorCode.Authentication, retryable: false);

    public static TranslationStreamEvent RateLimited() =>
        TranslationStreamEvent.Failed("翻译服务请求过于频繁。", QueryErrorCode.RateLimited, retryable: true);

    public static TranslationStreamEvent Timeout() =>
        TranslationStreamEvent.Failed("翻译服务请求超时。", QueryErrorCode.Timeout, retryable: true);

    public static TranslationStreamEvent Network() =>
        TranslationStreamEvent.Failed("无法连接翻译服务。", QueryErrorCode.Network, retryable: true);

    public static TranslationStreamEvent ProviderUnavailable() =>
        TranslationStreamEvent.Failed("翻译服务暂时不可用。", QueryErrorCode.ProviderUnavailable, retryable: true);

    public static TranslationStreamEvent Internal() =>
        TranslationStreamEvent.Failed("翻译服务返回了无效响应。", QueryErrorCode.Internal, retryable: false);

    public static TranslationStreamEvent FromHttpStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Authentication(),
            (HttpStatusCode)429 => RateLimited(),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => Timeout(),
            _ when (int)statusCode >= 500 => ProviderUnavailable(),
            _ when (int)statusCode >= 400 && (int)statusCode <= 499 => InvalidRequest(),
            _ => Internal(),
        };

    public static TranslationStreamEvent FromException(
        Exception exception,
        CancellationToken callerToken,
        CancellationToken timeoutToken) =>
        exception switch
        {
            OperationCanceledException when callerToken.IsCancellationRequested =>
                TranslationStreamEvent.Cancelled(),
            OperationCanceledException when timeoutToken.IsCancellationRequested => Timeout(),
            OperationCanceledException => TranslationStreamEvent.Cancelled(),
            HttpRequestException => Network(),
            IOException => Network(),
            _ => Internal(),
        };
}
