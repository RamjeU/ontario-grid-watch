namespace GridWatch.Core.Parsing;

/// <summary>
/// Parsed rows plus any rows that were skipped and why. Parsers never throw on a single bad row:
/// one malformed hour should not stop a year of data from loading, but it should be visible.
/// </summary>
public sealed record ParseResult<T>(IReadOnlyList<T> Items, IReadOnlyList<string> Warnings);

public sealed class IesoFormatException(string message) : Exception(message);
