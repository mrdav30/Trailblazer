namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Immutable validation result describing whether an active mantle may continue.
/// </summary>
public readonly struct MantleValidationSnapshot
{
    /// <summary>
    /// Reusable allow snapshot.
    /// </summary>
    public static readonly MantleValidationSnapshot Continue = new(true);

    /// <summary>
    /// Reusable cancel snapshot.
    /// </summary>
    public static readonly MantleValidationSnapshot Cancel = new(false);

    /// <summary>
    /// Initializes a new mantle validation snapshot.
    /// </summary>
    public MantleValidationSnapshot(bool canContinueMantle)
    {
        CanContinueMantle = canContinueMantle;
    }

    /// <summary>
    /// Gets a value indicating whether the mantle action can be continued.
    /// </summary>
    public bool CanContinueMantle { get; }
}
