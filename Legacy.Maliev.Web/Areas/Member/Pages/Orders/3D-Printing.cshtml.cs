using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

public sealed class Additive(
    IAccountSessionManager sessionManager,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionService submissionService)
    : MemberOrderCreatePageModel(CustomerOrderKind.Additive, sessionManager, catalogClient, submissionService);
