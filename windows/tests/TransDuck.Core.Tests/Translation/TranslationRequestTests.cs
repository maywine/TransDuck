// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class TranslationRequestTests
{
    [Fact]
    public void Validate_AllowsAbsoluteHttpEndpointAndPositiveTimeout()
    {
        var request = CreateValidRequest();

        request.Validate();
    }

    [Theory]
    [InlineData("ftp://example.test/translations")]
    [InlineData("file:///translations")]
    public void Validate_RejectsNonHttpEndpoint(string endpoint)
    {
        var request = CreateValidRequest() with { Endpoint = new Uri(endpoint) };

        var exception = Assert.Throws<ArgumentException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Endpoint), exception.ParamName);
    }

    [Fact]
    public void Validate_RejectsRelativeEndpoint()
    {
        var request = CreateValidRequest() with
        {
            Endpoint = new Uri("translations", UriKind.Relative),
        };

        var exception = Assert.Throws<ArgumentException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Endpoint), exception.ParamName);
    }

    [Fact]
    public void Validate_RejectsNullEndpoint()
    {
        var request = CreateValidRequest() with { Endpoint = null! };

        var exception = Assert.Throws<ArgumentNullException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Endpoint), exception.ParamName);
    }

    [Fact]
    public void Validate_RejectsNullCredentials()
    {
        var request = CreateValidRequest() with { Credentials = null! };

        var exception = Assert.Throws<ArgumentNullException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Credentials), exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsMissingModel(string? model)
    {
        var request = CreateValidRequest() with { Model = model! };

        var exception = Assert.ThrowsAny<ArgumentException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Model), exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsMissingText(string? text)
    {
        var request = CreateValidRequest() with { Text = text! };

        var exception = Assert.ThrowsAny<ArgumentException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Text), exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NonPositiveTimeouts))]
    public void Validate_RejectsNonPositiveTimeout(TimeSpan timeout)
    {
        var request = CreateValidRequest() with { Timeout = timeout };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(request.Validate);

        Assert.Equal(nameof(TranslationRequest.Timeout), exception.ParamName);
    }

    [Fact]
    public void PrintableRepresentations_RedactApiKey()
    {
        const string apiKey = "unit-test-key-that-must-not-be-printed";
        var credentials = new TranslationCredentials(apiKey);
        var request = CreateValidRequest() with { Credentials = credentials };

        var credentialsText = credentials.ToString();
        var requestText = request.ToString();

        Assert.True(credentials.HasApiKey);
        Assert.Contains("***redacted***", credentialsText);
        Assert.False(credentialsText.Contains(apiKey, StringComparison.Ordinal));
        Assert.False(requestText.Contains(apiKey, StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> NonPositiveTimeouts =>
    [
        [TimeSpan.Zero],
        [TimeSpan.FromMilliseconds(-1)],
    ];

    private static TranslationRequest CreateValidRequest() => new(
        new Uri("https://example.test/v1/chat/completions"),
        "test-model",
        "source text",
        "en",
        "zh-Hans",
        new TranslationCredentials(null),
        TimeSpan.FromSeconds(1));
}
