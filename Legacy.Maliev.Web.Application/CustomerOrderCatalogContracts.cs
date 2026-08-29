namespace Legacy.Maliev.Web.Application;

/// <summary>Describes one manufacturing process shown in a member order form.</summary>
public sealed record CustomerOrderCatalogProcess(int Id, int CategoryId, string Name);

/// <summary>Describes one material group shown in a member order form.</summary>
public sealed record CustomerOrderCatalogMaterialGroup(int Id, string Name);

/// <summary>Describes one material shown in a member order form.</summary>
public sealed record CustomerOrderCatalogMaterial(int Id, int MaterialGroupId, string Name);

/// <summary>Describes one selectable material color or surface finish.</summary>
public sealed record CustomerOrderCatalogOption(int Id, string Name);

/// <summary>Describes a currency accepted by the legacy order contract.</summary>
public sealed record CustomerOrderCurrency(int Id, string ShortName);

/// <summary>Describes one accepted order attachment format.</summary>
public sealed record CustomerOrderFileFormat(int Id, string? Name, string? Extension);

/// <summary>Contains the selection data required to render one member order flow.</summary>
public sealed record CustomerOrderCatalog(
    IReadOnlyList<CustomerOrderCatalogProcess> Processes,
    IReadOnlyList<CustomerOrderCatalogMaterialGroup> MaterialGroups,
    IReadOnlyList<CustomerOrderCatalogMaterial> Materials,
    IReadOnlyList<CustomerOrderFileFormat> FileFormats);

/// <summary>Contains options associated with one selected material.</summary>
public sealed record CustomerOrderMaterialOptions(
    IReadOnlyList<CustomerOrderCatalogOption> Colors,
    IReadOnlyList<CustomerOrderCatalogOption> SurfaceFinishes);

/// <summary>Represents a member order catalog lookup outcome.</summary>
public sealed record CustomerOrderCatalogResult(
    CustomerOrderCatalog? Catalog,
    bool ServiceAvailable,
    bool Authorized);

/// <summary>Represents a selected-material option lookup outcome.</summary>
public sealed record CustomerOrderMaterialOptionsResult(
    CustomerOrderMaterialOptions? Options,
    bool ServiceAvailable,
    bool Authorized);

/// <summary>Reads the existing Legacy Order and Catalog service contracts for member order forms.</summary>
public interface ICustomerOrderCatalogClient
{
    /// <summary>Gets the process, material, group, and file-format data used by the selected order flow.</summary>
    Task<CustomerOrderCatalogResult> GetAsync(
        CustomerOrderKind kind,
        CancellationToken cancellationToken);

    /// <summary>Gets colors and surface finishes linked to the selected material.</summary>
    Task<CustomerOrderMaterialOptionsResult> GetMaterialOptionsAsync(
        int materialId,
        CancellationToken cancellationToken);

    /// <summary>Resolves one order currency by its stable short name.</summary>
    Task<ServiceResponse<CustomerOrderCurrency>> GetCurrencyAsync(
        string shortName,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
