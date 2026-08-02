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

[Authorize]
[RequestFormLimits(MultipartBodyLengthLimit = 220_200_960)]
[RequestSizeLimit(220_200_960)]
public abstract class MemberOrderCreatePageModel(
    CustomerOrderKind kind,
    IAccountSessionManager sessionManager,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionService submissionService) : PageModel
{
    private const long MaximumUploadBytes = 200L * 1024 * 1024;

    public MemberOrderCreationDisplayModel DisplayModel { get; private set; } = default!;

    [BindProperty, Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [BindProperty, StringLength(500)] public string? Description { get; set; }
    [BindProperty, Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
    [BindProperty] public int ProcessId { get; set; }
    [BindProperty] public int MaterialId { get; set; }
    [BindProperty] public int ColorId { get; set; }
    [BindProperty] public int SurfaceFinishId { get; set; }
    [BindProperty] public bool AllowSocialMedia { get; set; }
    [BindProperty] public bool AcceptTermsAndConditions { get; set; }
    [BindProperty] public bool IsMetric { get; set; } = true;
    [BindProperty] public decimal? Width { get; set; }
    [BindProperty] public decimal? Length { get; set; }
    [BindProperty] public decimal? Height { get; set; }
    [BindProperty] public string OperationId { get; set; } = Guid.NewGuid().ToString("D");
    [BindProperty] public List<IFormFile> Files { get; set; } = [];

    [TempData] public string? Notification { get; set; }

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
            ModelState.AddModelError(nameof(Files), "The total upload size cannot exceed 200 MB.");
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
            $"Approximate dimensions: {Width:0.##} W x {Length:0.##} L x {Height:0.##} H {unit}.");
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
