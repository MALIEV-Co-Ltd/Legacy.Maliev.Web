using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>
/// Public, secret-free configuration for the LINE LIFF friendship bridge.
/// </summary>
public sealed partial class LineLiffOptions
{
    /// <summary>
    /// The configuration section containing the public LIFF application identifier.
    /// </summary>
    public const string SectionName = "LineLiff";

    /// <summary>
    /// Gets or sets the public LIFF application identifier.
    /// </summary>
    public string LiffId { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a valid public identifier enables the bridge.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(LiffId) && IsValid;

    /// <summary>
    /// Gets a value indicating whether the optional identifier is empty or bounded and safe.
    /// </summary>
    public bool IsValid => string.IsNullOrWhiteSpace(LiffId) || LiffIdPattern().IsMatch(LiffId);

    [GeneratedRegex("^[0-9]{6,20}-[A-Za-z0-9_-]{4,64}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex LiffIdPattern();
}
