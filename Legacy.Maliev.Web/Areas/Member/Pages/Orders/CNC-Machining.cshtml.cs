using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

/// <summary>Selects the machining catalogue and submission flow for a member order.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="catalogClient">The typed client supplying permitted order processes, materials, and file formats.</param>
/// <param name="submissionService">The service coordinating persistence and follow-up for the owned submission.</param>
public sealed class Machining(
    IAccountSessionManager sessionManager,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionService submissionService)
    : MemberOrderCreatePageModel(CustomerOrderKind.Machining, sessionManager, catalogClient, submissionService);
