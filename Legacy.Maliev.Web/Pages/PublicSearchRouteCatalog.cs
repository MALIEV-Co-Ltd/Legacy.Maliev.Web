// <copyright file="PublicSearchRouteCatalog.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages
{
    using Microsoft.AspNetCore.Http;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Xml.Linq;

    /// <summary>
    /// Declares the complete set of stable public routes intended for search indexing.
    /// </summary>
    internal static class PublicSearchRouteCatalog
    {
        /// <summary>
        /// Gets the canonical public search route inventory.
        /// </summary>
        internal static IReadOnlyList<PublicSearchRoute> Routes { get; } = new[]
        {
            new PublicSearchRoute("/", "weekly", 1.0),
            new PublicSearchRoute("/services", "monthly", 0.9),
            new PublicSearchRoute("/services/custom-manufacturing", "monthly", 0.9),
            new PublicSearchRoute("/services/cnc-machining", "monthly", 0.9),
            new PublicSearchRoute("/services/3d-printing", "monthly", 0.9),
            new PublicSearchRoute("/services/3d-scanning", "monthly", 0.9),
            new PublicSearchRoute("/about", "monthly", 0.8),
            new PublicSearchRoute("/about/socialmedia", "monthly", 0.7),
            new PublicSearchRoute("/contact", "monthly", 0.8),
            new PublicSearchRoute("/career", "monthly", 0.7),
            new PublicSearchRoute("/quotation", "yearly", 0.8),
            new PublicSearchRoute("/knowledges", "monthly", 0.8),
            new PublicSearchRoute("/knowledges/guidelines", "monthly", 0.7),
            new PublicSearchRoute("/knowledges/workflow", "monthly", 0.7),
            new PublicSearchRoute("/knowledges/specifications", "monthly", 0.8),
            new PublicSearchRoute("/knowledges/specifications/cnc-machining", "monthly", 0.8),
            new PublicSearchRoute("/knowledges/specifications/3d-printing", "monthly", 0.8),
            new PublicSearchRoute("/knowledges/specifications/3d-scanning", "monthly", 0.8),
            new PublicSearchRoute("/legal", "yearly", 0.5),
            new PublicSearchRoute("/legal/privacypolicy", "yearly", 0.5),
            new PublicSearchRoute("/legal/termsconditions", "yearly", 0.5),
            new PublicSearchRoute("/legal/nondisclosureagreement", "yearly", 0.5),
        };

        private static IReadOnlyDictionary<string, string> CanonicalPaths { get; } = Routes
            .ToDictionary(route => route.Path, route => route.Path, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Finds the exact declared spelling of a known public route.
        /// </summary>
        /// <param name="path">The normalized incoming path to look up.</param>
        /// <param name="canonicalPath">The exact declared path, when known.</param>
        /// <returns><see langword="true" /> when the path is in the public route inventory.</returns>
        internal static bool TryGetCanonicalPath(string path, out string canonicalPath)
        {
            if (CanonicalPaths.TryGetValue(path, out var declaredPath))
            {
                canonicalPath = declaredPath;
                return true;
            }

            canonicalPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Represents one stable, indexable public route.
    /// </summary>
    internal sealed class PublicSearchRoute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PublicSearchRoute"/> class.
        /// </summary>
        /// <param name="path">The canonical application path.</param>
        /// <param name="changeFrequency">The expected change frequency.</param>
        /// <param name="priority">The sitemap priority.</param>
        internal PublicSearchRoute(string path, string changeFrequency, double priority)
        {
            this.Path = path;
            this.ChangeFrequency = changeFrequency;
            this.Priority = priority;
        }

        /// <summary>
        /// Gets the canonical application path.
        /// </summary>
        internal string Path { get; }

        /// <summary>
        /// Gets the absolute canonical URL.
        /// </summary>
        internal string Location => CanonicalUrlPolicy.GetCanonicalUrl(new PathString(this.Path));

        /// <summary>
        /// Gets the expected change frequency.
        /// </summary>
        internal string ChangeFrequency { get; }

        /// <summary>
        /// Gets the sitemap priority.
        /// </summary>
        internal double Priority { get; }

        /// <summary>
        /// Gets the invariant decimal priority emitted in the sitemap.
        /// </summary>
        internal string FormattedPriority => this.Priority.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Produces the exact XML response consumed by crawlers and regression tests.
    /// </summary>
    internal static class SitemapXmlRenderer
    {
        /// <summary>
        /// Renders a sitemap document from the declared public route inventory.
        /// </summary>
        /// <param name="routes">The public routes to render.</param>
        /// <returns>A UTF-8 XML sitemap document.</returns>
        internal static string Render(IReadOnlyList<PublicSearchRoute> routes)
        {
            XNamespace sitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
            XNamespace xhtmlNamespace = "http://www.w3.org/1999/xhtml";
            XElement root = new XElement(
                sitemapNamespace + "urlset",
                routes.Select(route =>
                    new XElement(
                        sitemapNamespace + "url",
                        new XElement(sitemapNamespace + "loc", route.Location),
                        new XElement(
                            xhtmlNamespace + "link",
                            new XAttribute("rel", "alternate"),
                            new XAttribute("hreflang", "en"),
                            new XAttribute("href", CanonicalUrlPolicy.GetLocalizedUrl(route.Path, "en"))),
                        new XElement(
                            xhtmlNamespace + "link",
                            new XAttribute("rel", "alternate"),
                            new XAttribute("hreflang", "th"),
                            new XAttribute("href", CanonicalUrlPolicy.GetLocalizedUrl(route.Path, "th"))),
                        new XElement(
                            xhtmlNamespace + "link",
                            new XAttribute("rel", "alternate"),
                            new XAttribute("hreflang", "x-default"),
                            new XAttribute("href", CanonicalUrlPolicy.GetLocalizedUrl(route.Path, "th"))),
                        new XElement(sitemapNamespace + "changefreq", route.ChangeFrequency),
                        new XElement(sitemapNamespace + "priority", route.FormattedPriority))));

            return string.Concat(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                root.ToString(SaveOptions.DisableFormatting));
        }
    }
}
