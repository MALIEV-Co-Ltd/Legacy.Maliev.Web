using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

public sealed class Scanning(
    IAccountSessionManager sessionManager,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionService submissionService)
    : MemberOrderCreatePageModel(CustomerOrderKind.Scanning, sessionManager, catalogClient, submissionService);
