namespace Legacy.Maliev.Web.Application;

public sealed record CustomerTokenSet(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset RefreshExpiresAt);

public sealed record CustomerAuthenticationResult(
    CustomerTokenSet? Tokens,
    bool ServiceAvailable,
    int? DatabaseId = null);

public sealed record CustomerIdentityRegistration(
    bool Succeeded,
    string? IdentityId,
    int? DatabaseId,
    string? Email);

public sealed record CustomerActionChallenge(
    bool Accepted,
    string? Token,
    bool ServiceAvailable,
    bool Authorized);

public interface ICustomerAuthenticationClient
{
    Task<CustomerAuthenticationResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<CustomerAuthenticationResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    Task<CustomerIdentityRegistration> RegisterAsync(
        int databaseId,
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<CustomerActionChallenge> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken);

    Task<bool> CompleteEmailConfirmationAsync(
        string email,
        string token,
        CancellationToken cancellationToken);

    Task<CustomerActionChallenge> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken);

    Task<bool> CompletePasswordResetAsync(
        string email,
        string token,
        string password,
        CancellationToken cancellationToken);
}

public sealed record CustomerProfile(
    int Id,
    string FirstName,
    string LastName,
    string Email);

public sealed record CustomerProfileResult(
    CustomerProfile? Customer,
    bool ServiceAvailable,
    bool Authorized);

public interface ICustomerProfileClient
{
    Task<CustomerProfileResult> CreateAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken);
}

public sealed record CustomerAddress(
    int Id,
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerAccountDetails(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Telephone,
    string? Mobile,
    string? Fax,
    string Email,
    DateTime? DateOfBirth,
    int? CompanyId,
    int? BillingAddressId,
    int? ShippingAddressId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    CustomerAddress? BillingAddress,
    object? Company,
    CustomerAddress? ShippingAddress);

public sealed record CustomerAddressProfile(CustomerAccountDetails Customer);

public sealed record CustomerAddressProfileResult(
    CustomerAddressProfile? Profile,
    bool ServiceAvailable,
    bool Authorized);

public sealed record CustomerAddressInput(
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId);

public sealed record CustomerAddressUpdate(
    CustomerAddressInput Billing,
    CustomerAddressInput Shipping);

public sealed record CustomerAddressOperationResult(
    bool Succeeded,
    bool ServiceAvailable,
    bool Authorized);

public interface ICustomerAccountClient
{
    Task<CustomerAddressProfileResult> GetAddressProfileAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<CustomerAddressOperationResult> UpdateAddressesAsync(
        int customerId,
        CustomerAddressUpdate update,
        CancellationToken cancellationToken);
}
