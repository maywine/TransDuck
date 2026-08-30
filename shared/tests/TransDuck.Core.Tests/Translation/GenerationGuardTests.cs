// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class GenerationGuardTests
{
    [Fact]
    public void StartNewGeneration_SupersedesPreviousGeneration()
    {
        var guard = new GenerationGuard();

        var first = guard.StartNewGeneration();
        var second = guard.StartNewGeneration();

        Assert.True(guard.IsCurrent(second));
        Assert.False(guard.IsCurrent(first));
        Assert.True(second > first);
    }

    [Fact]
    public void InvalidateCurrent_MakesActiveGenerationStale()
    {
        var guard = new GenerationGuard();

        var active = guard.StartNewGeneration();
        guard.InvalidateCurrent();
        var replacement = guard.StartNewGeneration();

        Assert.False(guard.IsCurrent(active));
        Assert.True(guard.IsCurrent(replacement));
        Assert.True(replacement > active);
    }
}
