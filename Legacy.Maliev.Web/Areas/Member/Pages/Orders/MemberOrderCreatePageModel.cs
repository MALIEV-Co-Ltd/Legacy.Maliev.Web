using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

/// <summary>Validates catalogue selections and uploaded files before submitting an owned member order with an operation identifier.</summary>
/// <param name="kind">The manufacturing service whose catalogue and submission rules apply.</param>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="catalogClient">The typed client supplying permitted order processes, materials, and file formats.</param>
/// <param name="submissionService">The service coordinating persistence and follow-up for the owned submission.</param>
[Authorize]
[RequestFormLimits(MultipartBodyLengthLimit = 115_343_360)]
[RequestSizeLimit(115_343_360)]
public abstract class MemberOrderCreatePageModel(
    CustomerOrderKind kind,
    IAccountSessionManager sessionManager,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionService submissionService) : PageModel
{
    private const long MaximumUploadBytes = 100L * 1024 * 1024;

    /// <summary>The order form fields, catalogue options, and validation feedback.</summary>
    public MemberOrderCreationDisplayModel DisplayModel { get; private set; } = default!;

    /// <summary>The customer-supplied order title used to identify the manufacturing request.</summary>
    [BindProperty, Required(ErrorMessage = "Please give your order a name"), StringLength(200)] public string Name { get; set; } = string.Empty;
    /// <summary>The optional customer instructions accompanying the manufacturing request.</summary>
    [BindProperty, StringLength(500)] public string? Description { get; set; }
    /// <summary>The requested number of manufactured items; scanning submissions use one.</summary>
    [BindProperty, Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")] public int Quantity { get; set; } = 1;
    /// <summary>The selected manufacturing process identifier validated against the current order catalogue.</summary>
    [BindProperty] public int ProcessId { get; set; }
    /// <summary>The selected material identifier validated against the current order catalogue.</summary>
    [BindProperty] public int MaterialId { get; set; }
    /// <summary>The selected additive material colour identifier validated against available material options.</summary>
    [BindProperty] public int ColorId { get; set; }
    /// <summary>The selected surface-finish identifier validated against available material options.</summary>
    [BindProperty] public int SurfaceFinishId { get; set; }
    /// <summary>Whether the customer permits social-media use associated with the submitted order.</summary>
    [BindProperty] public bool AllowSocialMedia { get; set; }
    /// <summary>Whether the customer accepted the required terms before order submission.</summary>
    [BindProperty] public bool AcceptTermsAndConditions { get; set; }
    /// <summary>Whether scanning dimensions are expressed in millimetres rather than inches.</summary>
    [BindProperty] public bool IsMetric { get; set; } = true;
    /// <summary>The optional approximate scanning width in the selected dimension unit.</summary>
    [BindProperty] public decimal? Width { get; set; }
    /// <summary>The optional approximate scanning length in the selected dimension unit.</summary>
    [BindProperty] public decimal? Length { get; set; }
    /// <summary>The optional approximate scanning height in the selected dimension unit.</summary>
    [BindProperty] public decimal? Height { get; set; }
    /// <summary>The operation identifier used to correlate and validate this owned submission.</summary>
    [BindProperty] public string OperationId { get; set; } = Guid.NewGuid().ToString("D");
    /// <summary>The uploaded request files validated before submission or attachment follow-up.</summary>
    [BindProperty] public List<IFormFile> Files { get; set; } = [];

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData] public string? Notification { get; set; }

    /// <summary>Loads the service catalogue and resolves optional process and material preselections.</summary>
    /// <param name="process">The requested process name used to preselect the current catalogue option.</param>
    /// <param name="material">The requested material name used to select a catalogue or estimate option.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The order form with its current catalogue and display projection.</returns>
    public async Task<IActionResult> OnGetAsync(
        string? process,
        string? material,
        CancellationToken cancellationToken)
    {
        var catalog = (await catalogClient.GetAsync(kind, cancellationToken)).Catalog;
        ProcessId = catalog?.Processes.FirstOrDefault(item =>
            string.Equals(item.Name, process, StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0;
        MaterialId = catalog?.Materials.FirstOrDefault(item =>
            string.Equals(item.Name, material, StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0;
        await LoadDisplayModelAsync(cancellationToken, catalog);
        return Page();
    }

    /// <summary>Validates consent, catalogue options, dimensions, and uploads before submitting an owned order.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The created-order redirect, the form with errors or partial-outcome guidance, or an authentication challenge.</returns>
    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(OperationId, out var operationId) || operationId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(OperationId), "The order operation is invalid. Please reload the page and try again.");
        }

        if (!AcceptTermsAndConditions)
        {
            ModelState.AddModelError(nameof(AcceptTermsAndConditions), "You must agree to the terms of service.");
        }

        if (Files.Sum(file => file.Length) > MaximumUploadBytes)
        {
            ModelState.AddModelError(nameof(Files), "The total upload size cannot exceed 100 MB.");
        }

        var catalogResult = await catalogClient.GetAsync(kind, cancellationToken);
        var catalog = catalogResult.Catalog;
        var options = await ValidateCatalogSelectionsAsync(catalog, cancellationToken);
        if (kind == CustomerOrderKind.Scanning)
        {
            ValidateScanningDimensions();
        }

        if (!ModelState.IsValid || catalog is null)
        {
            await LoadDisplayModelAsync(cancellationToken, catalog, options);
            return Page();
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        var customerEmail = User.FindFirstValue(ClaimTypes.Email);
        if (customerId is null || string.IsNullOrWhiteSpace(customerEmail)) return Challenge();

        var processId = kind == CustomerOrderKind.Scanning
            ? catalog.Processes.Single().Id
            : ProcessId;
        var draft = new CustomerOrderDraft(
            kind,
            Name.Trim(),
            BuildDescription(),
            processId,
            kind == CustomerOrderKind.Scanning ? null : MaterialId,
            kind == CustomerOrderKind.Scanning ? null : SurfaceFinishId,
            kind == CustomerOrderKind.Additive ? ColorId : null,
            kind == CustomerOrderKind.Scanning ? 1 : Quantity,
            AllowSocialMedia,
            Files.Select(static file => (ICustomerOrderUploadFile)new FormFileUpload(file)).ToArray());
        var result = await submissionService.SubmitAsync(
            customerId.Value,
            customerEmail,
            draft,
            operationId,
            cancellationToken);
        if (result.Succeeded && result.OrderId is > 0)
        {
            Notification = "Your order has been submitted successfully.";
            return RedirectToPage("/Orders/View", new { area = "Member", itemID = result.OrderId.Value });
        }

        ModelState.AddModelError(
            string.Empty,
            result.Persisted
                ? "Your order was saved, but one or more follow-up steps are incomplete. Please contact us before retrying."
                : result.Conflict
                    ? "This order submission conflicts with an existing request. Please reload and try again."
                    : result.ServiceAvailable
                        ? "Your order could not be submitted."
                        : "Order processing is temporarily unavailable.");
        await LoadDisplayModelAsync(cancellationToken, catalog);
        return Page();
    }

    private async Task<CustomerOrderMaterialOptions?> ValidateCatalogSelectionsAsync(
        CustomerOrderCatalog? catalog,
        CancellationToken cancellationToken)
    {
        if (catalog is null)
        {
            ModelState.AddModelError(string.Empty, "Order options are temporarily unavailable.");
            return null;
        }

        if (kind == CustomerOrderKind.Scanning)
        {
            if (catalog.Processes.Count != 1) ModelState.AddModelError(nameof(ProcessId), "The scanning process is unavailable.");
        }
        else
        {
            if (catalog.Processes.All(process => process.Id != ProcessId)) ModelState.AddModelError(nameof(ProcessId), "Please select a valid process.");
            if (catalog.Materials.All(material => material.Id != MaterialId)) ModelState.AddModelError(nameof(MaterialId), "Please select a valid material.");
        }

        CustomerOrderMaterialOptions? options = null;
        if (kind != CustomerOrderKind.Scanning && catalog.Materials.Any(material => material.Id == MaterialId))
        {
            var result = await catalogClient.GetMaterialOptionsAsync(MaterialId, cancellationToken);
            options = result.Options;
            if (options is null)
            {
                ModelState.AddModelError(nameof(MaterialId), "Material options are temporarily unavailable.");
            }
            else
            {
                if (kind == CustomerOrderKind.Additive && options.Colors.All(color => color.Id != ColorId))
                {
                    ModelState.AddModelError(nameof(ColorId), "Please select a valid color.");
                }

                if (options.SurfaceFinishes.All(finish => finish.Id != SurfaceFinishId))
                {
                    ModelState.AddModelError(nameof(SurfaceFinishId), "Please select a valid surface finish.");
                }
            }
        }

        var accepted = catalog.FileFormats
            .Select(format => format.Extension?.Trim())
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Files.Any(file => !accepted.Contains(Path.GetExtension(Path.GetFileName(file.FileName)))))
        {
            ModelState.AddModelError(nameof(Files), "One or more files use an unsupported format.");
        }

        return options;
    }

    private void ValidateScanningDimensions()
    {
        if (Width is not > 0) ModelState.AddModelError(nameof(Width), "Width must be greater than zero.");
        if (Length is not > 0) ModelState.AddModelError(nameof(Length), "Length must be greater than zero.");
        if (Height is not > 0) ModelState.AddModelError(nameof(Height), "Height must be greater than zero.");
    }

    private string? BuildDescription()
    {
        if (kind != CustomerOrderKind.Scanning) return string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        var unit = IsMetric ? "mm" : "in";
        var dimensions = string.Create(
            CultureInfo.InvariantCulture,
            $"Approximate dimensions: {Width} W x {Length} L x {Height} H {unit}.");
        return string.IsNullOrWhiteSpace(Description) ? dimensions : $"{dimensions} {Description.Trim()}";
    }

    private async Task LoadDisplayModelAsync(
        CancellationToken cancellationToken,
        CustomerOrderCatalog? knownCatalog = null,
        CustomerOrderMaterialOptions? knownOptions = null)
    {
        var catalog = knownCatalog;
        if (catalog is null)
        {
            var result = await catalogClient.GetAsync(kind, cancellationToken);
            catalog = result.Catalog;
            if (catalog is null && !ModelState.ContainsKey(string.Empty))
            {
                ModelState.AddModelError(string.Empty, "Order options are temporarily unavailable.");
            }
        }

        var options = knownOptions ?? (MaterialId > 0
            ? await catalogClient.GetMaterialOptionsAsync(MaterialId, cancellationToken)
            : null)?.Options;
        DisplayModel = new(
            kind,
            catalog,
            options?.Colors ?? [],
            options?.SurfaceFinishes ?? [],
            Name,
            Description,
            Quantity,
            ProcessId,
            MaterialId,
            ColorId,
            SurfaceFinishId,
            AllowSocialMedia,
            AcceptTermsAndConditions,
            IsMetric,
            Width,
            Length,
            Height,
            OperationId,
            Notification,
            ModelState.SelectMany(entry => entry.Value?.Errors ?? [])
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The order form contains invalid values." : error.ErrorMessage)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private sealed class FormFileUpload(IFormFile file) : ICustomerOrderUploadFile
    {
        public string FileName => file.FileName;
        public string ContentType => string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        public long Length => file.Length;
        public Stream OpenReadStream() => file.OpenReadStream();
    }
}
