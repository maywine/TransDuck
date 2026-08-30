using System.Text;
using System.Runtime.InteropServices;
using TransDuck.Platform.MacOS.Interop;

namespace TransDuck.Platform.MacOS.Ocr;

public enum MacOcrStatus
{
    Succeeded,
    NoText,
    LanguageUnavailable,
    Cancelled,
    Unsupported,
    Failed,
}

public sealed record MacOcrResult(MacOcrStatus Status, string? Text = null)
{
    public bool Succeeded => Status == MacOcrStatus.Succeeded && Text is not null;
}

public enum VisionBackendStatus
{
    Succeeded,
    Unsupported,
    Failed,
}

public sealed record VisionBackendResult(VisionBackendStatus Status, string? Text = null);

public sealed record VisionRecognitionOptions(string Language, bool UseLanguageCorrection);

public interface IVisionTextRecognizerBackend
{
    VisionBackendResult Recognize(string imagePath, VisionRecognitionOptions options);
}

public sealed class VisionOcrService
{
    private readonly IVisionTextRecognizerBackend _backend;

    public VisionOcrService()
        : this(new VisionFrameworkTextRecognizerBackend())
    {
    }

    public VisionOcrService(IVisionTextRecognizerBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public async Task<MacOcrResult> RecognizeAsync(
        string imagePath,
        string languageTag,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new MacOcrResult(MacOcrStatus.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return new MacOcrResult(MacOcrStatus.Failed);
        }

        var recognitionLanguage = MapLanguage(languageTag);
        if (recognitionLanguage is null)
        {
            return new MacOcrResult(MacOcrStatus.LanguageUnavailable);
        }

        try
        {
            var options = new VisionRecognitionOptions(
                recognitionLanguage,
                UseLanguageCorrection: !string.Equals(
                    recognitionLanguage,
                    "zh-Hans",
                    StringComparison.Ordinal));
            var result = await Task.Run(
                () => _backend.Recognize(Path.GetFullPath(imagePath), options),
                CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return new MacOcrResult(MacOcrStatus.Cancelled);
            }

            return result.Status switch
            {
                VisionBackendStatus.Succeeded when string.IsNullOrWhiteSpace(result.Text) =>
                    new MacOcrResult(MacOcrStatus.NoText),
                VisionBackendStatus.Succeeded =>
                    new MacOcrResult(MacOcrStatus.Succeeded, result.Text!.TrimEnd()),
                VisionBackendStatus.Unsupported => new MacOcrResult(MacOcrStatus.Unsupported),
                _ => new MacOcrResult(MacOcrStatus.Failed),
            };
        }
        catch (PlatformNotSupportedException)
        {
            return new MacOcrResult(MacOcrStatus.Unsupported);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new MacOcrResult(MacOcrStatus.Failed);
        }
    }

    private static string? MapLanguage(string? languageTag) =>
        languageTag?.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" => "en-US",
            "zh" or "zh-cn" or "zh-hans" or "zh-hans-cn" => "zh-Hans",
            _ => null,
        };
}

internal sealed class VisionFrameworkTextRecognizerBackend : IVisionTextRecognizerBackend
{
    public VisionBackendResult Recognize(string imagePath, VisionRecognitionOptions recognitionOptions)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            return new VisionBackendResult(VisionBackendStatus.Unsupported);
        }

        Frameworks.EnsureLoaded();
        using var pool = new ObjectiveCAutoreleasePool();
        using var coreFoundation = new CoreFoundationScope();
        IntPtr request = IntPtr.Zero;
        IntPtr handler = IntPtr.Zero;
        try
        {
            var requestClass = GetClass("VNRecognizeTextRequest");
            request = ObjectiveCNative.SendIntPtr(
                ObjectiveCNative.SendIntPtr(requestClass, Selectors.Alloc),
                Selectors.Init);
            if (request == IntPtr.Zero)
            {
                return new VisionBackendResult(VisionBackendStatus.Unsupported);
            }

            ObjectiveCNative.SendVoidByte(
                request,
                Selectors.SetUsesLanguageCorrection,
                recognitionOptions.UseLanguageCorrection ? (byte)1 : (byte)0);

            var language = coreFoundation.String(recognitionOptions.Language);
            var languages = ObjectiveCNative.SendIntPtr(
                GetClass("NSArray"),
                Selectors.ArrayWithObject,
                language);
            ObjectiveCNative.SendVoidIntPtr(request, Selectors.SetRecognitionLanguages, languages);

            var path = coreFoundation.String(Path.GetFullPath(imagePath));
            var url = ObjectiveCNative.SendIntPtr(GetClass("NSURL"), Selectors.FileUrlWithPath, path);
            var imageOptions = ObjectiveCNative.SendIntPtr(GetClass("NSDictionary"), Selectors.Dictionary);
            var handlerClass = GetClass("VNImageRequestHandler");
            handler = ObjectiveCNative.SendIntPtr(
                ObjectiveCNative.SendIntPtr(handlerClass, Selectors.Alloc),
                Selectors.InitWithUrlOptions,
                url,
                imageOptions);
            if (handler == IntPtr.Zero)
            {
                return new VisionBackendResult(VisionBackendStatus.Failed);
            }

            var requests = ObjectiveCNative.SendIntPtr(
                GetClass("NSArray"),
                Selectors.ArrayWithObject,
                request);
            if (!ObjectiveCNative.SendBool(
                    handler,
                    Selectors.PerformRequestsError,
                    requests,
                    IntPtr.Zero))
            {
                return new VisionBackendResult(VisionBackendStatus.Failed);
            }

            var results = ObjectiveCNative.SendIntPtr(request, Selectors.Results);
            if (results == IntPtr.Zero)
            {
                return new VisionBackendResult(VisionBackendStatus.Succeeded, string.Empty);
            }

            var text = new StringBuilder();
            var resultCount = ObjectiveCNative.SendUIntPtr(results, Selectors.Count);
            for (nuint index = 0; index < resultCount; index++)
            {
                var observation = ObjectiveCNative.SendIntPtr(results, Selectors.ObjectAtIndex, index);
                var candidates = ObjectiveCNative.SendIntPtr(observation, Selectors.TopCandidates, (nuint)1);
                if (candidates == IntPtr.Zero ||
                    ObjectiveCNative.SendUIntPtr(candidates, Selectors.Count) == 0)
                {
                    continue;
                }

                var candidate = ObjectiveCNative.SendIntPtr(candidates, Selectors.ObjectAtIndex, (nuint)0);
                var nativeString = ObjectiveCNative.SendIntPtr(candidate, Selectors.String);
                var line = CoreFoundationNative.CopyString(nativeString);
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                }

                text.Append(line);
            }

            return new VisionBackendResult(VisionBackendStatus.Succeeded, text.ToString());
        }
        finally
        {
            if (handler != IntPtr.Zero)
            {
                ObjectiveCNative.SendVoid(handler, ObjectiveCAutoreleasePool.Selectors.Release);
            }

            if (request != IntPtr.Zero)
            {
                ObjectiveCNative.SendVoid(request, ObjectiveCAutoreleasePool.Selectors.Release);
            }
        }
    }

    private static IntPtr GetClass(string name)
    {
        var value = ObjectiveCNative.objc_getClass(name);
        if (value == IntPtr.Zero)
        {
            throw new PlatformNotSupportedException("A required macOS Vision class is unavailable.");
        }

        return value;
    }

    private static class Selectors
    {
        internal static readonly IntPtr Alloc = ObjectiveCAutoreleasePool.Selectors.Alloc;
        internal static readonly IntPtr Init = ObjectiveCAutoreleasePool.Selectors.Init;
        internal static readonly IntPtr ArrayWithObject = ObjectiveCNative.sel_registerName("arrayWithObject:");
        internal static readonly IntPtr Count = ObjectiveCNative.sel_registerName("count");
        internal static readonly IntPtr Dictionary = ObjectiveCNative.sel_registerName("dictionary");
        internal static readonly IntPtr FileUrlWithPath = ObjectiveCNative.sel_registerName("fileURLWithPath:");
        internal static readonly IntPtr InitWithUrlOptions = ObjectiveCNative.sel_registerName("initWithURL:options:");
        internal static readonly IntPtr ObjectAtIndex = ObjectiveCNative.sel_registerName("objectAtIndex:");
        internal static readonly IntPtr PerformRequestsError = ObjectiveCNative.sel_registerName("performRequests:error:");
        internal static readonly IntPtr Results = ObjectiveCNative.sel_registerName("results");
        internal static readonly IntPtr SetRecognitionLanguages = ObjectiveCNative.sel_registerName("setRecognitionLanguages:");
        internal static readonly IntPtr SetUsesLanguageCorrection = ObjectiveCNative.sel_registerName("setUsesLanguageCorrection:");
        internal static readonly IntPtr String = ObjectiveCNative.sel_registerName("string");
        internal static readonly IntPtr TopCandidates = ObjectiveCNative.sel_registerName("topCandidates:");
    }

    private static class Frameworks
    {
        private const string Foundation =
            "/System/Library/Frameworks/Foundation.framework/Foundation";
        private const string Vision =
            "/System/Library/Frameworks/Vision.framework/Vision";
        private static readonly IntPtr FoundationHandle = NativeLibrary.Load(Foundation);
        private static readonly IntPtr VisionHandle = NativeLibrary.Load(Vision);

        internal static void EnsureLoaded()
        {
            _ = FoundationHandle;
            _ = VisionHandle;
        }
    }
}
