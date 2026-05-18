namespace ModelMeister.Cli;

/// <summary>
/// Process exit codes consumed by CI pipelines.
/// Treat these as a public contract — do not change values without coordinating with
/// the GitHub workflows under <c>new/.github/workflows/</c>.
/// </summary>
public static class ExitCodes
{
    /// <summary>Operation succeeded; no further action required.</summary>
    public const int Success = 0;

    /// <summary>
    /// Diff found changes (<c>diff --fail-on-changes</c>) or an apply was aborted
    /// before mutations completed. Used by <c>model-diff.yml</c> to gate merges.
    /// </summary>
    public const int ChangesPending = 1;

    /// <summary>The user supplied missing, conflicting, or otherwise invalid arguments.</summary>
    public const int UsageError = 2;

    /// <summary>Model validation produced one or more errors. Used by <c>model-validate.yml</c>.</summary>
    public const int ValidationFailed = 3;

    /// <summary>An operational failure occurred — connection error, partial apply, IO error.</summary>
    public const int OperationFailed = 4;
}
