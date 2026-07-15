namespace Legacy.Maliev.Web.Application;

public enum CareerSort
{
    JobId_Ascending,
    JobId_Descending,
    JobCreatedDate_Ascending,
    JobCreatedDate_Descending
}

public sealed record CareerLevel(
    int Id,
    string? Name,
    string? Description,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CareerOffer(
    int Id,
    int LevelId,
    string? Title,
    string? Introduction,
    string? Description,
    string? Prerequisites,
    string? WhatWeOffer,
    string? Location,
    bool? IsFilled,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    CareerLevel? Level);

public sealed record CareerOfferPage(
    IReadOnlyList<CareerOffer> Items,
    int PageIndex,
    int TotalPages,
    int TotalItems,
    bool HasPreviousPage,
    bool HasNextPage)
{
    public static CareerOfferPage Empty(int pageIndex) =>
        new([], pageIndex, 0, 0, false, false);
}

public sealed record CareerListing(
    IReadOnlyList<CareerLevel> Levels,
    CareerOfferPage Offers,
    bool ServiceAvailable);

public sealed record ServiceResponse<T>(T? Value, bool ServiceAvailable)
    where T : class;

public interface ICareerClient
{
    Task<CareerListing> GetListingAsync(
        CareerSort sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken);
}
