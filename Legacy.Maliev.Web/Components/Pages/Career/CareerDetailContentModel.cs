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
    bool IsFilled,
    string SocialUrl)
{
    public static CareerDetailContentModel Create(CareerOffer offer, string socialUrl) =>
        new(
            offer.Id,
            offer.Title,
            offer.Introduction,
            offer.Description,
            offer.Prerequisites,
            offer.WhatWeOffer,
            offer.Location,
            offer.Level?.Name ?? "not specified",
            offer.IsFilled == true,
            socialUrl);
}
