// <copyright file="CanonicalUrlPolicy.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web
{
    using Legacy.Maliev.Web.Pages;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the application-owned canonical origin and public path policy.
    /// </summary>
    public static class CanonicalUrlPolicy
    {
        /// <summary>
        /// The only public origin emitted in canonical URLs and sitemaps.
        /// </summary>
        public const string CanonicalOrigin = "https://www.maliev.com";

        /// <summary>
        /// The canonical public host name.
        /// </summary>
        public const string CanonicalHost = "www.maliev.com";

        private static readonly IReadOnlySet<string> AlternateHosts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "maliev.com",
            };

        private static readonly IReadOnlyDictionary<string, string> RouteAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/3d-printing"] = "/services/3d-printing",
                ["/3d-scanning"] = "/services/3d-scanning",
                ["/cnc-machining"] = "/services/cnc-machining",
            };

        /// <summary>
        /// Gets the canonical path for an incoming public path.
        /// </summary>
        /// <param name="requestPath">The incoming request path.</param>
        /// <returns>The declared canonical path for a known public route; otherwise, the original path.</returns>
        public static string GetCanonicalPath(PathString requestPath)
        {
            string path = requestPath.HasValue ? requestPath.Value ?? "/" : "/";
            path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            string lookupPath = path;

            if (lookupPath.Length > 1)
            {
                lookupPath = lookupPath.TrimEnd('/');
                lookupPath = lookupPath.Length == 0 ? "/" : lookupPath;
            }

            if (RouteAliases.TryGetValue(lookupPath, out var canonicalAlias))
            {
                return canonicalAlias;
            }

            return PublicSearchRouteCatalog.TryGetCanonicalPath(lookupPath, out string canonicalPath)
                ? canonicalPath
                : path;
        }

        /// <summary>
        /// Builds an absolute canonical URL. Query strings are intentionally excluded.
        /// </summary>
        /// <param name="requestPath">The request path to canonicalize.</param>
        /// <returns>An absolute URL on the canonical MALIEV origin.</returns>
        public static string GetCanonicalUrl(PathString requestPath)
        {
            string canonicalPath = GetCanonicalPath(requestPath);
            string encodedPath = new PathString(canonicalPath).ToUriComponent();
            return string.Concat(CanonicalOrigin, encodedPath);
        }

        /// <summary>
        /// Builds a stable localized URL for the supported public cultures.
        /// Thai is the default document at the clean canonical path; English uses
        /// the explicit query-string culture contract understood by the app.
        /// </summary>
        /// <param name="requestPath">The request path to canonicalize.</param>
        /// <param name="culture">The two-letter public culture name.</param>
        /// <returns>An absolute localized URL on the canonical MALIEV origin.</returns>
        public static string GetLocalizedUrl(PathString requestPath, string culture)
        {
            string canonicalUrl = GetCanonicalUrl(requestPath);
            return string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase)
                ? string.Concat(canonicalUrl, "?culture=en")
                : canonicalUrl;
        }

        /// <summary>
        /// Determines the one redirect required to reach the canonical host and path.
        /// </summary>
        /// <param name="request">The current request after trusted-edge HTTPS enforcement.</param>
        /// <param name="location">The redirect location, including the original query string.</param>
        /// <returns><see langword="true"/> when the request needs canonicalization.</returns>
        internal static bool TryGetRedirectLocation(HttpRequest request, out string location)
        {
            string requestPath = request.Path.HasValue ? request.Path.Value : "/";
            string canonicalPath = GetCanonicalPath(request.Path);
            bool pathChanged = !string.Equals(requestPath, canonicalPath, StringComparison.Ordinal);
            string requestHost = request.Host.Host.TrimEnd('.');
            bool isCanonicalHost = string.Equals(requestHost, CanonicalHost, StringComparison.OrdinalIgnoreCase);
            bool isAlternateHost = AlternateHosts.Contains(requestHost);
            bool isConfiguredPublicHost = isCanonicalHost || isAlternateHost;
            bool hasCanonicalPort = !request.Host.Port.HasValue || request.Host.Port == 443;

            if (isConfiguredPublicHost
                && (!isCanonicalHost || !hasCanonicalPort || pathChanged))
            {
                location = string.Concat(GetCanonicalUrl(request.Path), request.QueryString.Value);
                return true;
            }

            if (pathChanged)
            {
                location = string.Concat(new PathString(canonicalPath).ToUriComponent(), request.QueryString.Value);
                return true;
            }

            location = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Applies the canonical URL policy to public GET and HEAD requests.
    /// </summary>
    internal sealed class CanonicalUrlRedirectMiddleware
    {
        private readonly RequestDelegate next;

        /// <summary>
        /// Initializes a new instance of the <see cref="CanonicalUrlRedirectMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the request pipeline.</param>
        public CanonicalUrlRedirectMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        /// <summary>
        /// Redirects non-canonical read requests and leaves mutations unchanged.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing completion of the request.</returns>
        public Task InvokeAsync(HttpContext context)
        {
            bool isPublicRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            if (isPublicRead && CanonicalUrlPolicy.TryGetRedirectLocation(context.Request, out string location))
            {
                context.Response.Redirect(location, permanent: true);
                return Task.CompletedTask;
            }

            return this.next(context);
        }
    }
}
