using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerQuotationSortQueryTests
{
    [Theory]
    [InlineData("0", "QuotationId_Ascending")]
    [InlineData("1", "QuotationId_Descending")]
    [InlineData("2", "QuotationCreatedDate_Ascending")]
    [InlineData("3", "QuotationCreatedDate_Descending")]
    [InlineData("4", "QuotationModifiedDate_Ascending")]
    [InlineData("5", "QuotationModifiedDate_Descending")]
    public void DefinedProducerNamesAndNumbersResolveCanonically(string number, string name)
    {
        Assert.True(CustomerQuotationSortQuery.TryResolve(number, out var numeric));
        Assert.Equal(name, numeric);
        Assert.True(CustomerQuotationSortQuery.TryResolve(name, out var canonical));
        Assert.Equal(name, canonical);
        Assert.True(CustomerQuotationSortQuery.TryResolve(name.ToLowerInvariant(), out var insensitive));
        Assert.Equal(name, insensitive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void OmittedSortUsesExistingCreatedDescendingDefault(string? value)
    {
        Assert.True(CustomerQuotationSortQuery.TryResolve(value, out var canonical));
        Assert.Equal("QuotationCreatedDate_Descending", canonical);
    }

    [Theory]
    [InlineData("QuotationExpirationDate_Ascending")]
    [InlineData("QuotationQuotedAmount_Descending")]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("QuotationId_Ascending,QuotationCreatedDate_Descending")]
    [InlineData("unknown")]
    public void UndefinedOrCompositeSortIsRejected(string value)
    {
        Assert.False(CustomerQuotationSortQuery.TryResolve(value, out var canonical));
        Assert.Empty(canonical);
    }
}
