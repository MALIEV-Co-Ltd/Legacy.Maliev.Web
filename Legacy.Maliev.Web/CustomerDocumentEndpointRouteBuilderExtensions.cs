using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Legacy.Maliev.Web;

public static class CustomerDocumentEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCustomerDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/member/documents").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapGet("/{documentId:guid}/versions/{versionId:guid}/receipt", ReceiptAsync);
        group.MapGet("/{documentId:guid}/versions/{versionId:guid}/download", DownloadAsync);
        group.MapPost("/upload", UploadAsync).DisableAntiforgery(); // Explicit validation before any form/body processing below.
        group.MapPost("/{documentId:guid}/versions", ReplaceAsync).DisableAntiforgery();
        return endpoints;
    }

    private static Task<IResult> UploadAsync(HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, IAntiforgery antiforgery, CancellationToken token) => UploadCoreAsync(null, context, sessions, client, antiforgery, token);
    private static Task<IResult> ReplaceAsync(Guid documentId, HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, IAntiforgery antiforgery, CancellationToken token) => documentId == Guid.Empty ? Task.FromResult<IResult>(Results.BadRequest()) : UploadCoreAsync(documentId, context, sessions, client, antiforgery, token);
    private static async Task<IResult> UploadCoreAsync(Guid? documentId, HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, IAntiforgery antiforgery, CancellationToken token)
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 20 * 1024 * 1024 + 64 * 1024;
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(); }
        var session = await SessionAsync(context, sessions, token);
        if (session is null) return Results.Unauthorized();
        if (!context.Request.HasFormContentType) return Results.BadRequest();
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(token); }
        catch (InvalidDataException) { return Results.StatusCode(413); }
        if (form.Files.Count != 1) return Results.BadRequest();
        var file = form.Files[0];
        if (file.Length is <= 0 or > 20 * 1024 * 1024) return Results.StatusCode(413);
        if (file.ContentType is not ("application/pdf" or "image/png" or "image/jpeg")) return Results.StatusCode(415);
        var kind = form["Kind"].ToString(); var title = form["Title"].ToString();
        if (kind is not ("Nda" or "Corporate" or "BillingInstruction" or "Shipment" or "Release" or "Acceptance" or "Evidence") || string.IsNullOrWhiteSpace(title) || title.Length > 250 || !Guid.TryParse(form["IdempotencyKey"], out var key)) return Results.BadRequest();
        long? expectedRevision = null;
        if (documentId is not null) { if (!long.TryParse(form["ExpectedRevision"], out var revision) || revision <= 0) return Results.BadRequest(); expectedRevision = revision; }
        await using var stream = file.OpenReadStream();
        var result = await client.UploadAsync(session.Value.CustomerId, documentId, expectedRevision, kind, title, stream, file.FileName, file.ContentType, key.ToString("D"), session.Value.AccessToken, token);
        return result.StatusCode == 200 && result.Value is { } value && value.CustomerId == session.Value.CustomerId && value.DocumentId != Guid.Empty && (documentId is null || value.DocumentId == documentId) && value.VersionId != Guid.Empty && value.VersionNumber > 0 && value.Revision > 0 && CustomerDocumentContractGuard.Digest(value.ContentSha256) ? Results.Redirect("/Member/Documents?uploaded=true") : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }

    private static async Task<(int CustomerId, string AccessToken)?> SessionAsync(HttpContext context, IAccountSessionManager sessions, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.User.Identity?.IsAuthenticated != true || context.User.FindFirst("identity_kind")?.Value != "customer") return null;
        var accessToken = await sessions.GetAccessTokenAsync(context, token);
        var customerId = await sessions.GetCustomerDatabaseIdAsync(context, token);
        return customerId is > 0 && !string.IsNullOrWhiteSpace(accessToken) ? (customerId.Value, accessToken) : null;
    }

    private static async Task<IResult> ListAsync(HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, CancellationToken token)
    {
        var session = await SessionAsync(context, sessions, token);
        if (session is null) return Results.Unauthorized();
        var result = await client.ListAsync(session.Value.CustomerId, session.Value.AccessToken, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
        // An Internal result is evidence of a broken upstream boundary. Return no partial metadata.
        if (result.Value.Count > 50 || result.Value.Any(x => !CustomerDocumentContractGuard.MemberSummary(x, session.Value.CustomerId))) return Results.StatusCode(503);
        return Results.Json(result.Value.Select(x => new { x.DocumentId, x.Kind, x.Title, x.Revision }));
    }

    private static async Task<IResult> ReceiptAsync(Guid documentId, Guid versionId, HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, CancellationToken token)
    {
        if (documentId == Guid.Empty || versionId == Guid.Empty) return Results.BadRequest();
        var session = await SessionAsync(context, sessions, token);
        if (session is null) return Results.Unauthorized();
        var result = await client.ReceiptAsync(session.Value.CustomerId, documentId, versionId, session.Value.AccessToken, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
        var value = result.Value;
        if (value.CustomerId != session.Value.CustomerId || value.DocumentId != documentId || value.VersionId != versionId || value.Revision <= 0 || !CustomerDocumentContractGuard.Digest(value.ContentSha256) || !CustomerDocumentContractGuard.KnownKind(value.Kind) || !CustomerDocumentContractGuard.VerificationEvidence(value.VerificationStatus, value.VerifiedBySubject, value.VerifiedAtUtc)) return Results.StatusCode(503);
        return Results.Json(new MemberDocumentReceipt(value.DocumentId, value.VersionId, value.Kind, value.ContentSha256, value.VerificationStatus, value.Revision));
    }

    private static async Task<IResult> DownloadAsync(Guid documentId, Guid versionId, HttpContext context, IAccountSessionManager sessions, ICustomerDocumentClient client, CancellationToken token)
    {
        if (documentId == Guid.Empty || versionId == Guid.Empty) return Results.BadRequest();
        var session = await SessionAsync(context, sessions, token);
        if (session is null) return Results.Unauthorized();
        var result = await client.DownloadAsync(session.Value.CustomerId, documentId, versionId, session.Value.AccessToken, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(result.Value, "application/octet-stream", $"document-{documentId:D}-{versionId:D}", enableRangeProcessing: false);
    }
}
