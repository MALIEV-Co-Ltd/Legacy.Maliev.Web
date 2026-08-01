// <copyright file="ServiceFinderMetadataEnvelope.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

#nullable disable

namespace Legacy.Maliev.Web.Pages.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads, writes, and preserves the reserved service-finder metadata envelope.
    /// </summary>
    public static class ServiceFinderMetadataEnvelope
    {
        /// <summary>
        /// The reserved metadata source marker.
        /// </summary>
        public const string Source = "service_finder";

        /// <summary>
        /// The current metadata envelope version.
        /// </summary>
        public const int Version = 1;

        private static readonly IReadOnlyDictionary<string, string[]> AllowedAnswerIds =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "files", new[] { "files-3d", "files-2d", "files-image", "files-none", "files-real-part" } },
                { "service", new[] { "service-machining", "service-3d", "service-molding", "service-unsure" } },
                { "material", new[] { "material-metal", "material-standard-plastic", "material-resin", "material-plastic", "material-silicone", "material-unsure" } },
                { "quantity", new[] { "quantity-1-10", "quantity-11-100", "quantity-101-1000", "quantity-over-1000" } },
                { "end-use", new[] { "use-prototype", "use-industrial", "use-replacement", "use-consumer" } },
            };

        private static readonly IReadOnlyDictionary<string, string[]> AllowedOptionalAnswerIds =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "performance", new[] { "performance-strength", "performance-appearance", "performance-flexibility", "performance-temperature", "performance-unsure" } },
                { "environment", new[] { "environment-indoor", "environment-outdoor", "environment-wet", "environment-heat-chemical", "environment-unsure" } },
            };

        private static readonly string[] AllowedServiceIds =
        {
            "custom",
            "cnc",
            "printing",
            "scanning",
            "design",
            "silicone",
            "injection",
        };

        /// <summary>
        /// Creates a compact envelope containing stable IDs only.
        /// </summary>
        /// <param name="answers">The stable finder answers.</param>
        /// <param name="recommendedServiceIds">The recommended service IDs.</param>
        /// <param name="finderPath">The ordered recommended path.</param>
        /// <returns>The versioned JSON envelope.</returns>
        public static string CreateJson(
            IReadOnlyDictionary<string, string> answers,
            IReadOnlyList<string> recommendedServiceIds,
            IReadOnlyList<string> finderPath)
        {
            return JsonSerializer.Serialize(new
            {
                source = Source,
                version = Version,
                answers,
                recommended_service_ids = recommendedServiceIds,
                finder_path = finderPath,
            });
        }

        /// <summary>
        /// Determines whether a finder answer is part of the shared stable-ID contract.
        /// </summary>
        /// <param name="key">The finder question key.</param>
        /// <param name="value">The proposed stable answer ID.</param>
        /// <returns><see langword="true" /> when the answer is allowlisted.</returns>
        public static bool IsAllowedAnswer(string key, string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && AllowedAnswerIds.TryGetValue(key, out string[] allowedValues)
                && allowedValues.Contains(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether an optional finder answer is part of the shared stable-ID contract.
        /// </summary>
        /// <param name="key">The optional finder question key.</param>
        /// <param name="value">The proposed stable answer ID.</param>
        /// <returns><see langword="true" /> when the optional answer is allowlisted.</returns>
        public static bool IsAllowedOptionalAnswer(string key, string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && AllowedOptionalAnswerIds.TryGetValue(key, out string[] allowedValues)
                && allowedValues.Contains(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether a service ID is part of the shared finder contract.
        /// </summary>
        /// <param name="value">The proposed stable service ID.</param>
        /// <returns><see langword="true" /> when the service is allowlisted.</returns>
        public static bool IsAllowedServiceId(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && AllowedServiceIds.Contains(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Reads and validates a persisted finder envelope.
        /// </summary>
        /// <param name="rawValue">The existing internal-comment value.</param>
        /// <param name="metadata">The validated metadata, when present.</param>
        /// <returns><see langword="true" /> when the value is a valid finder envelope.</returns>
        public static bool TryRead(string rawValue, out ServiceFinderMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawValue);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("source", out JsonElement source)
                    || !string.Equals(source.GetString(), Source, StringComparison.Ordinal)
                    || !root.TryGetProperty("version", out JsonElement version)
                    || version.GetInt32() != Version
                    || !root.TryGetProperty("answers", out JsonElement answers)
                    || answers.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                Dictionary<string, string> answerValues = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string[]> entry in AllowedAnswerIds)
                {
                    if (!answers.TryGetProperty(entry.Key, out JsonElement answer)
                        || answer.ValueKind != JsonValueKind.String
                        || !IsAllowedAnswer(entry.Key, answer.GetString()))
                    {
                        return false;
                    }

                    answerValues[entry.Key] = answer.GetString();
                }

                foreach (KeyValuePair<string, string[]> entry in AllowedOptionalAnswerIds)
                {
                    if (!answers.TryGetProperty(entry.Key, out JsonElement answer))
                    {
                        continue;
                    }

                    if (answer.ValueKind != JsonValueKind.String
                        || !IsAllowedOptionalAnswer(entry.Key, answer.GetString()))
                    {
                        return false;
                    }

                    answerValues[entry.Key] = answer.GetString();
                }

                if (!TryReadServiceIds(root, "recommended_service_ids", out List<string> recommendations)
                    || !TryReadServiceIds(root, "finder_path", out List<string> path)
                    || recommendations.Count == 0
                    || path.Count == 0
                    || path.Any(pathId => !recommendations.Contains(pathId, StringComparer.Ordinal)))
                {
                    return false;
                }

                string operatorComment = root.TryGetProperty("operator_comment", out JsonElement comment)
                    && comment.ValueKind == JsonValueKind.String
                    ? comment.GetString()
                    : string.Empty;

                metadata = new ServiceFinderMetadata(answerValues, recommendations, path, operatorComment);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Merges an operator note into a finder envelope without discarding the attribution.
        /// </summary>
        /// <param name="existingValue">The currently persisted internal-comment value.</param>
        /// <param name="operatorComment">The operator-authored note.</param>
        /// <returns>The preserved envelope or the ordinary note when no envelope exists.</returns>
        public static string MergeOperatorComment(string existingValue, string operatorComment)
        {
            if (!TryRead(existingValue, out _))
            {
                return operatorComment?.Trim();
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(existingValue);
                Dictionary<string, object> envelope = document.RootElement.EnumerateObject()
                    .Where(property => !string.Equals(property.Name, "operator_comment", StringComparison.Ordinal))
                    .ToDictionary(property => property.Name, property => (object)property.Value.Clone(), StringComparer.Ordinal);

                string normalizedComment = operatorComment?.Trim();
                if (!string.IsNullOrEmpty(normalizedComment))
                {
                    envelope["operator_comment"] = normalizedComment;
                }

                return JsonSerializer.Serialize(envelope);
            }
            catch (JsonException)
            {
                return operatorComment?.Trim();
            }
        }

        private static bool TryReadServiceIds(JsonElement root, string propertyName, out List<string> values)
        {
            values = new List<string>();
            if (!root.TryGetProperty(propertyName, out JsonElement array)
                || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() is < 1 or > 4)
            {
                return false;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                string value = item.GetString();
                if (!IsAllowedServiceId(value)
                    || values.Contains(value, StringComparer.Ordinal))
                {
                    return false;
                }

                values.Add(value);
            }

            return true;
        }
    }

    /// <summary>
    /// A validated finder metadata value for operator display and preservation.
    /// </summary>
    public sealed class ServiceFinderMetadata
    {
        private static readonly IReadOnlyDictionary<string, string> DisplayLabels =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "files-3d", "3D model available" },
                { "files-2d", "2D drawing available" },
                { "files-image", "Reference image available" },
                { "files-none", "No production file yet" },
                { "files-real-part", "Physical part available" },
                { "service-machining", "CNC machining" },
                { "service-3d", "3D service" },
                { "service-molding", "Molding" },
                { "service-unsure", "Needs service guidance" },
                { "material-metal", "Metal" },
                { "material-standard-plastic", "Standard plastic" },
                { "material-resin", "Resin" },
                { "material-plastic", "Engineering plastic" },
                { "material-silicone", "Silicone" },
                { "material-unsure", "Material undecided" },
                { "quantity-1-10", "1-10 units" },
                { "quantity-11-100", "11-100 units" },
                { "quantity-101-1000", "101-1,000 units" },
                { "quantity-over-1000", "More than 1,000 units" },
                { "use-prototype", "Prototype" },
                { "use-industrial", "Industrial use" },
                { "use-replacement", "Replacement part" },
                { "use-consumer", "Consumer product" },
                { "performance", "Performance" },
                { "performance-strength", "Strength and durability" },
                { "performance-appearance", "Appearance and fine detail" },
                { "performance-flexibility", "Flexibility and soft touch" },
                { "performance-temperature", "Heat or chemical resistance" },
                { "performance-unsure", "Performance undecided" },
                { "environment-indoor", "Indoor and controlled" },
                { "environment-outdoor", "Outdoor or UV exposure" },
                { "environment-wet", "Water or moisture" },
                { "environment-heat-chemical", "Heat, chemicals, or harsh conditions" },
                { "environment-unsure", "Environment undecided" },
                { "environment", "Environment" },
                { "custom", "Project review" },
                { "cnc", "CNC machining" },
                { "printing", "3D printing" },
                { "scanning", "3D scanning" },
                { "design", "3D design" },
                { "silicone", "Silicone casting" },
                { "injection", "Low-volume injection molding" },
            };

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceFinderMetadata" /> class.
        /// </summary>
        /// <param name="answers">The stable answers.</param>
        /// <param name="recommendedServiceIds">The recommended IDs.</param>
        /// <param name="finderPath">The ordered path.</param>
        /// <param name="operatorComment">The operator note.</param>
        internal ServiceFinderMetadata(
            IReadOnlyDictionary<string, string> answers,
            IReadOnlyList<string> recommendedServiceIds,
            IReadOnlyList<string> finderPath,
            string operatorComment)
        {
            this.Answers = answers;
            this.RecommendedServiceIds = recommendedServiceIds;
            this.FinderPath = finderPath;
            this.OperatorComment = operatorComment ?? string.Empty;
        }

        /// <summary>
        /// Gets the stable finder answers.
        /// </summary>
        public IReadOnlyDictionary<string, string> Answers { get; }

        /// <summary>
        /// Gets the recommended service IDs.
        /// </summary>
        public IReadOnlyList<string> RecommendedServiceIds { get; }

        /// <summary>
        /// Gets the ordered finder path.
        /// </summary>
        public IReadOnlyList<string> FinderPath { get; }

        /// <summary>
        /// Gets the operator-authored note.
        /// </summary>
        public string OperatorComment { get; }

        /// <summary>
        /// Gets a human-readable stable-ID summary for the intranet operator view.
        /// </summary>
        public string Summary => string.Join(
            "; ",
            new[]
            {
                $"Files: {GetDisplayLabel(this.Answers["files"])}",
                $"Intent: {GetDisplayLabel(this.Answers["service"])}",
                $"Material: {GetDisplayLabel(this.Answers["material"])}",
                $"Quantity: {GetDisplayLabel(this.Answers["quantity"])}",
                $"End use: {GetDisplayLabel(this.Answers["end-use"])}",
            }
            .Concat(new[] { "performance", "environment" }
                .Where(this.Answers.ContainsKey)
                .Select(key => $"{GetDisplayLabel(key)}: {GetDisplayLabel(this.Answers[key])}"))
            .Concat(new[]
            {
                $"Recommended: {string.Join(", ", this.RecommendedServiceIds.Select(GetDisplayLabel))}",
                $"Path: {string.Join(" -> ", this.FinderPath.Select(GetDisplayLabel))}",
            }));

        private static string GetDisplayLabel(string stableId)
        {
            return DisplayLabels.TryGetValue(stableId, out string label) ? label : stableId;
        }
    }
}
