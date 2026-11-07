using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Career;

/// <summary>
/// Carries safe display text and hiring status for one public career offer.
/// </summary>
/// <param name="Id">The career offer identifier used by the detail route.</param>
/// <param name="Title">The role title, or null when not supplied.</param>
/// <param name="Introduction">The offer introduction normalized to safe display text.</param>
/// <param name="Description">The role responsibilities normalized to safe display text.</param>
/// <param name="Prerequisites">The applicant requirements normalized to safe display text.</param>
/// <param name="WhatWeOffer">The employment benefits normalized to safe display text.</param>
/// <param name="Location">The advertised work location, or null when absent.</param>
/// <param name="LevelName">The associated career level label or its unavailable fallback.</param>
/// <param name="IsFilled">Whether the position has been filled, or null when its status is unspecified.</param>
public sealed record CareerDetailContentModel(
    int Id,
    string? Title,
    string? Introduction,
    string? Description,
    string? Prerequisites,
    string? WhatWeOffer,
    string? Location,
    string? LevelName,
    bool? IsFilled)
{
    /// <summary>
    /// Projects a career offer into text-only detail content suitable for public rendering.
    /// </summary>
    /// <param name="offer">The published offer whose rich-text fields are converted to safe display text.</param>
    /// <returns>The display content and advertised hiring status.</returns>
    /// <param name="levels">The available career levels used to resolve the offer level.</param>
    public static CareerDetailContentModel Create(CareerOffer offer, IReadOnlyList<CareerLevel> levels) =>
        new(
            offer.Id,
            offer.Title,
            CareerOfferPresentation.ToSafeText(offer.Introduction),
            CareerOfferPresentation.ToSafeText(offer.Description),
            CareerOfferPresentation.ToSafeText(offer.Prerequisites),
            CareerOfferPresentation.ToSafeText(offer.WhatWeOffer),
            offer.Location,
            ResolveLevelName(offer.LevelId, levels),
            offer.IsFilled);

    private static string? ResolveLevelName(int levelId, IReadOnlyList<CareerLevel> levels)
    {
        var level = levels.SingleOrDefault(item => item.Id == levelId);
        return level is null ? "not specified" : level.Name;
    }
}
