// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Platform.Windows.Tests.Ocr;

public sealed class OcrFixtureAssetsTests
{
    [Theory]
    [InlineData(OcrFixtureAssets.CleanEnglish)]
    [InlineData(OcrFixtureAssets.CleanChinese)]
    [InlineData(OcrFixtureAssets.Mixed)]
    public async Task CanonicalFixture_HasExpectedHashAndPixels(string fileName)
    {
        OcrFixtureAssets.AssertCanonicalHash(fileName);
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(fileName);

        Assert.Equal(1800, bitmap.PixelWidth);
        Assert.Equal(360, bitmap.PixelHeight);
    }
}
