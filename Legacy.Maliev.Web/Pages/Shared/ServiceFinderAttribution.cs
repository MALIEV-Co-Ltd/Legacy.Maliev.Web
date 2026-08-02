// <copyright file="ServiceFinderAttribution.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

#nullable disable

namespace Legacy.Maliev.Web.Pages.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Validated, non-PII context produced by the public service finder.
    /// </summary>
    internal sealed class ServiceFinderAttribution
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceFinderAttribution" /> class.
        /// </summary>
        /// <param name="answers">The stable finder answer IDs.</param>
        /// <param name="recommendedServiceIds">The recommended service IDs.</param>
        /// <param name="finderPath">The ordered recommended service path.</param>
        private ServiceFinderAttribution(
            IReadOnlyDictionary<string, string> answers,
            IReadOnlyList<string> recommendedServiceIds,
            IReadOnlyList<string> finderPath)
        {
            this.Answers = answers;
            this.Intent = answers["service"];
            this.RecommendedServiceIds = recommendedServiceIds;
            this.FinderPath = finderPath;
        }

        /// <summary>
        /// Gets the stable finder answers.
        /// </summary>
        public IReadOnlyDictionary<string, string> Answers { get; }

        /// <summary>
        /// Gets the stable service-intent answer.
        /// </summary>
        public string Intent { get; }

        /// <summary>
        /// Gets the recommended service IDs.
        /// </summary>
        public IReadOnlyList<string> RecommendedServiceIds { get; }

        /// <summary>
        /// Gets the ordered service path used to reach the recommendation.
        /// </summary>
        public IReadOnlyList<string> FinderPath { get; }

        /// <summary>
        /// Gets a compact string suitable for the stable data-layer event contract.
        /// </summary>
        public string FinderPathValue => string.Join(">", this.FinderPath);

        /// <summary>
        /// Parses and validates finder values received from the browser.
        /// </summary>
        /// <param name="files">The files answer ID.</param>
        /// <param name="service">The service answer ID.</param>
        /// <param name="material">The material answer ID.</param>
        /// <param name="quantity">The quantity answer ID.</param>
        /// <param name="endUse">The end-use answer ID.</param>
        /// <param name="recommendedServiceIds">The comma-separated recommendation IDs.</param>
        /// <param name="finderPath">The comma-separated ordered path IDs.</param>
        /// <param name="attribution">The validated attribution, when all values are valid.</param>
        /// <returns><see langword="true" /> when a complete, allowlisted attribution was created.</returns>
        public static bool TryCreate(
            string files,
            string service,
            string material,
            string quantity,
            string endUse,
            string recommendedServiceIds,
            string finderPath,
            out ServiceFinderAttribution attribution)
        {
            return TryCreate(
                files,
                service,
                material,
                quantity,
                endUse,
                recommendedServiceIds,
                finderPath,
                null,
                null,
                out attribution);
        }

        /// <summary>
        /// Parses and validates finder values, including optional requirement answers.
        /// </summary>
        /// <param name="files">The files answer ID.</param>
        /// <param name="service">The service answer ID.</param>
        /// <param name="material">The material answer ID.</param>
        /// <param name="quantity">The quantity answer ID.</param>
        /// <param name="endUse">The end-use answer ID.</param>
        /// <param name="recommendedServiceIds">The comma-separated recommendation IDs.</param>
        /// <param name="finderPath">The comma-separated ordered path IDs.</param>
        /// <param name="performance">The optional performance answer ID.</param>
        /// <param name="environment">The optional environment answer ID.</param>
        /// <param name="attribution">The validated attribution, when all values are valid.</param>
        /// <returns><see langword="true" /> when a complete, allowlisted attribution was created.</returns>
        public static bool TryCreate(
            string files,
            string service,
            string material,
            string quantity,
            string endUse,
            string recommendedServiceIds,
            string finderPath,
            string performance,
            string environment,
            out ServiceFinderAttribution attribution)
        {
            attribution = null;
            if (!TryGetAllowedAnswer("files", files, out string filesValue)
                || !TryGetAllowedAnswer("service", service, out string serviceValue)
                || !TryGetAllowedAnswer("material", material, out string materialValue)
                || !TryGetAllowedAnswer("quantity", quantity, out string quantityValue)
                || !TryGetAllowedAnswer("end-use", endUse, out string endUseValue))
            {
                return false;
            }

            if (!TryGetAllowedOptionalAnswer("performance", performance, out string performanceValue)
                && !string.IsNullOrWhiteSpace(performance))
            {
                return false;
            }

            if (!TryGetAllowedOptionalAnswer("environment", environment, out string environmentValue)
                && !string.IsNullOrWhiteSpace(environment))
            {
                return false;
            }

            if (!TryParseServiceIds(recommendedServiceIds, out List<string> recommendations)
                || !TryParseServiceIds(finderPath, out List<string> path))
            {
                return false;
            }

            if (recommendations.Count == 0 && path.Count == 0)
            {
                return false;
            }

            if (recommendations.Count == 0)
            {
                recommendations = new List<string>(path);
            }

            if (path.Count == 0)
            {
                path = new List<string>(recommendations);
            }

            if (path.Any(pathId => !recommendations.Contains(pathId, StringComparer.Ordinal)))
            {
                return false;
            }

            List<string> expectedPath = ComputeExpectedFinderPath(filesValue, serviceValue, materialValue, quantityValue, endUseValue);
            bool pathMatchesAnswers = expectedPath.Count > 0
                ? expectedPath.SequenceEqual(path, StringComparer.Ordinal)
                : recommendations.SequenceEqual(path, StringComparer.Ordinal);
            if (!pathMatchesAnswers
                || expectedPath.Any(pathId => !recommendations.Contains(pathId, StringComparer.Ordinal)))
            {
                return false;
            }

            Dictionary<string, string> answers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "files", filesValue },
                { "service", serviceValue },
                { "material", materialValue },
                { "quantity", quantityValue },
                { "end-use", endUseValue },
            };

            if (!string.IsNullOrEmpty(performanceValue))
            {
                answers["performance"] = performanceValue;
            }

            if (!string.IsNullOrEmpty(environmentValue))
            {
                answers["environment"] = environmentValue;
            }

            attribution = new ServiceFinderAttribution(answers, recommendations, path);

            return true;
        }

        /// <summary>
        /// Serializes finder metadata for the existing quotation request internal-comment field.
        /// </summary>
        /// <returns>A versioned JSON metadata envelope containing stable IDs only.</returns>
        public string ToMetadataJson()
        {
            return ServiceFinderMetadataEnvelope.CreateJson(this.Answers, this.RecommendedServiceIds, this.FinderPath);
        }

        /// <summary>
        /// Validates the optional lead-event projection independently of the persisted metadata envelope.
        /// </summary>
        /// <param name="intent">The stable service-intent ID.</param>
        /// <param name="finderPath">The ordered path encoded with <c>&gt;</c> separators.</param>
        /// <returns><see langword="true" /> when both optional values are absent or allowlisted.</returns>
        internal static bool IsAllowedLeadValues(string intent, string finderPath)
        {
            if (string.IsNullOrEmpty(intent) && string.IsNullOrEmpty(finderPath))
            {
                return true;
            }

            if (string.IsNullOrEmpty(intent) || string.IsNullOrWhiteSpace(finderPath)
                || !ServiceFinderMetadataEnvelope.IsAllowedAnswer("service", intent))
            {
                return false;
            }

            string[] pathValues = finderPath.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pathValues.Length is < 1 or > 4)
            {
                return false;
            }

            return pathValues.All(ServiceFinderMetadataEnvelope.IsAllowedServiceId)
                && pathValues.Distinct(StringComparer.Ordinal).Count() == pathValues.Length;
        }

        private static bool TryGetAllowedAnswer(string key, string value, out string normalizedValue)
        {
            normalizedValue = value?.Trim();
            return !string.IsNullOrEmpty(normalizedValue)
                && ServiceFinderMetadataEnvelope.IsAllowedAnswer(key, normalizedValue);
        }

        private static bool TryGetAllowedOptionalAnswer(string key, string value, out string normalizedValue)
        {
            normalizedValue = value?.Trim();
            return string.IsNullOrEmpty(normalizedValue)
                || ServiceFinderMetadataEnvelope.IsAllowedOptionalAnswer(key, normalizedValue);
        }

        private static bool TryParseServiceIds(string rawValue, out List<string> serviceIds)
        {
            serviceIds = new List<string>();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return true;
            }

            string[] candidates = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (candidates.Length > 4)
            {
                return false;
            }

            foreach (string candidate in candidates)
            {
                if (!ServiceFinderMetadataEnvelope.IsAllowedServiceId(candidate))
                {
                    return false;
                }

                if (!serviceIds.Contains(candidate, StringComparer.Ordinal))
                {
                    serviceIds.Add(candidate);
                }
            }

            return true;
        }

        private static List<string> ComputeExpectedFinderPath(
            string files,
            string service,
            string material,
            string quantity,
            string endUse)
        {
            List<string> path = new List<string>();

            void AddUnique(string serviceId)
            {
                if (!path.Contains(serviceId, StringComparer.Ordinal))
                {
                    path.Add(serviceId);
                }
            }

            if (files == "files-real-part")
            {
                AddUnique("scanning");
            }
            else if (files == "files-none")
            {
                AddUnique(service == "service-machining" || endUse == "use-replacement" ? "scanning" : "design");
            }
            else if (files == "files-image" || files == "files-2d")
            {
                AddUnique("design");
            }

            if (service == "service-machining")
            {
                AddUnique("cnc");
            }
            else if (service == "service-3d")
            {
                if (files == "files-none" && endUse == "use-replacement")
                {
                    AddUnique("scanning");
                }

                AddUnique("printing");
            }
            else if (service == "service-molding")
            {
                AddUnique(material == "material-silicone" ? "silicone" : "injection");
            }
            else if (material == "material-silicone")
            {
                AddUnique("silicone");
            }
            else if (quantity == "quantity-101-1000")
            {
                AddUnique("injection");
            }
            else if (material == "material-metal")
            {
                AddUnique("cnc");
            }
            else
            {
                AddUnique("printing");
            }

            if (quantity == "quantity-over-1000")
            {
                path.RemoveAll(serviceId => serviceId == "injection");
            }

            return path;
        }
    }
}
