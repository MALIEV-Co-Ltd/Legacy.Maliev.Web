namespace Legacy.Maliev.Web.Application;

public sealed class ServiceEndpoints
{
    public Uri Auth { get; init; } = new("http://auth");

    public Uri Career { get; init; } = new("http://careers");

    public Uri Catalog { get; init; } = new("http://catalog");

    public Uri Contact { get; init; } = new("http://contacts");

    public Uri Country { get; init; } = new("http://countries");

    public Uri Customer { get; init; } = new("http://customers");

    public Uri Document { get; init; } = new("http://documents");

    public Uri File { get; init; } = new("http://files");

    public Uri Order { get; init; } = new("http://orders");

    public Uri Quotation { get; init; } = new("http://quotations");
}
