// <copyright file="QuotationRouteRedirectMiddleware.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Middleware
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Extensions;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Consolidates bounded legacy quotation paths into the non-indexable query-prefill utility.
    /// </summary>
    internal sealed class QuotationRouteRedirectMiddleware
    {
        private static readonly HashSet<string> SupportedServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "3d-design",
            "3d-printing",
            "3d-scanning",
            "cnc-machining",
            "custom-manufacturing",
            "low-volume-injection-molding",
            "silicone-casting",
        };

        private readonly RequestDelegate next;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuotationRouteRedirectMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next request delegate.</param>
        public QuotationRouteRedirectMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        /// <summary>
        /// Applies the legacy quotation path policy.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing the request.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/quotation", out PathString remaining))
            {
                await this.next(context);
                return;
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Robots-Tag"] = "noindex, follow";
                return Task.CompletedTask;
            });

            if (!remaining.HasValue)
            {
                await this.next(context);
                return;
            }

            string suffix = remaining.Value.Trim('/');
            if (suffix.Length == 0)
            {
                context.Response.Redirect(BuildBaseLocation(context.Request.Query), permanent: true);
                return;
            }

            if (!suffix.Contains("/", StringComparison.Ordinal) && SupportedServices.Contains(suffix))
            {
                context.Response.Redirect(BuildPrefillLocation(context.Request.Query, suffix.ToLowerInvariant()), permanent: true);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }

        private static string BuildBaseLocation(IQueryCollection query)
        {
            QueryBuilder builder = CopyQuery(query, includeItem: true);
            return string.Concat("/quotation", builder.ToQueryString().Value);
        }

        private static string BuildPrefillLocation(IQueryCollection query, string service)
        {
            QueryBuilder builder = CopyQuery(query, includeItem: false);
            builder.Add("item", service);
            return string.Concat("/quotation", builder.ToQueryString().Value);
        }

        private static QueryBuilder CopyQuery(IQueryCollection query, bool includeItem)
        {
            QueryBuilder builder = new();
            foreach (KeyValuePair<string, StringValues> pair in query)
            {
                if (!includeItem && string.Equals(pair.Key, "item", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string? value in pair.Value)
                {
                    if (value is not null)
                    {
                        builder.Add(pair.Key, value);
                    }
                }
            }

            return builder;
        }
    }
}
