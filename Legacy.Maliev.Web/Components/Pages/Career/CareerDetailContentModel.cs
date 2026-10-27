using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Career;

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
