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
    DateTime? ModifiedDate)
{
    public IReadOnlyList<CareerOffer>? Offers { get; init; } = [];
}

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
    where T : class
{
    /// <summary>In-process provenance; null retains the existing availability contract.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Net.HttpStatusCode? UpstreamStatus { get; init; }

    public bool Equals(ServiceResponse<T>? other) => other is not null
        && EqualityComparer<T?>.Default.Equals(Value, other.Value)
        && ServiceAvailable == other.ServiceAvailable;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = EqualityComparer<Type>.Default.GetHashCode(typeof(ServiceResponse<T>));
            hash = (hash * -1521134295) + (Value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(Value));
            return (hash * -1521134295) + EqualityComparer<bool>.Default.GetHashCode(ServiceAvailable);
        }
    }

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Value = ");
        builder.Append(Value);
        builder.Append(", ServiceAvailable = ");
        builder.Append(ServiceAvailable);
        return true;
    }
}

public interface ICareerClient
{
    Task<CareerListing> GetListingAsync(
        CareerSort sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
        CareerSort sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken);

    Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken);
}
