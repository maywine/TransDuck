// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Core.Persistence;

/// <summary>
/// Describes stable persistence outcomes without exposing filesystem or crypto details.
/// </summary>
public enum PersistenceStatus
{
    Succeeded,
    NotFound,
    Cancelled,
    InvalidData,
    UnsupportedVersion,
    CorruptData,
    IoFailure,
}

/// <summary>
/// Represents a non-value persistence operation outcome.
/// </summary>
public sealed record PersistenceResult(PersistenceStatus Status)
{
    /// <summary>Gets whether the operation completed successfully.</summary>
    public bool Succeeded => Status == PersistenceStatus.Succeeded;

    /// <summary>Creates a successful outcome.</summary>
    public static PersistenceResult Success() => new(PersistenceStatus.Succeeded);

    /// <summary>Creates an outcome for a stable status.</summary>
    public static PersistenceResult FromStatus(PersistenceStatus status) => new(status);
}

/// <summary>
/// Represents a persistence read outcome and its optional value.
/// </summary>
public sealed record PersistenceReadResult<TValue>(PersistenceStatus Status, TValue? Value = default)
    where TValue : class
{
    /// <summary>Gets whether a value was read successfully.</summary>
    public bool Succeeded => Status == PersistenceStatus.Succeeded && Value is not null;

    /// <summary>Creates a successful read outcome.</summary>
    public static PersistenceReadResult<TValue> Success(TValue value) =>
        new(PersistenceStatus.Succeeded, value);

    /// <summary>Creates an outcome without a value.</summary>
    public static PersistenceReadResult<TValue> FromStatus(PersistenceStatus status) => new(status);
}
