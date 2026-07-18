namespace Legacy.Maliev.Web.Middleware;

internal sealed class BuildIdentityHeaderMiddleware(RequestDelegate next, BuildIdentity identity)
{
    internal const string RepositoryHeader = "X-Maliev-Build-Repository";
    internal const string BranchHeader = "X-Maliev-Build-Branch";
    internal const string CommitHeader = "X-Maliev-Build-Commit";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers[RepositoryHeader] = identity.Repository;
        context.Response.Headers[BranchHeader] = identity.Branch;
        context.Response.Headers[CommitHeader] = identity.Commit;
        return next(context);
    }
}
