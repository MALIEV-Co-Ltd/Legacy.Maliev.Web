namespace Legacy.Maliev.Web.Application;

/// <summary>A CNC engineering-review request, never an order or payment instruction.</summary>
public sealed record CncRequestSubmission(QuotationRequestSubmission Contact, Guid JourneyId);

/// <summary>Distinguishes safe pre-send failures from potentially persisted requests.</summary>
public enum CncRequestOutcome
{
    NotSent,
    Unknown,
    Created,
}

/// <summary>The durable request identity is available only after a verified create response.</summary>
public sealed record CncRequestResult(CncRequestOutcome Outcome, int? RequestId = null);

/// <summary>Creates CNC engineering-review requests without replaying ambiguous writes.</summary>
public interface ICncRequestClient
{
    Task<CncRequestResult> CreateAsync(CncRequestSubmission submission, CancellationToken cancellationToken);
}
