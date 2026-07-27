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
    string LevelName,
    bool? IsFilled)
{
    public static CareerDetailContentModel Create(CareerOffer offer) =>
        new(
            offer.Id,
            offer.Title,
            CareerOfferPresentation.ToSafeText(offer.Introduction),
            CareerOfferPresentation.ToSafeText(offer.Description),
            CareerOfferPresentation.ToSafeText(offer.Prerequisites),
            CareerOfferPresentation.ToSafeText(offer.WhatWeOffer),
            offer.Location,
            offer.Level?.Name ?? "not specified",
            offer.IsFilled);
}
