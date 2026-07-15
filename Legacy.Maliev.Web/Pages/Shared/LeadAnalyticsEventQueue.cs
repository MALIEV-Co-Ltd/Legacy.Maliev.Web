// <copyright file="LeadAnalyticsEventQueue.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages.Shared
{
    using Microsoft.AspNetCore.Mvc.ViewFeatures;
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Queues persisted public-lead events for one-time rendering after post/redirect/get.
    /// </summary>
    public static class LeadAnalyticsEventQueue
    {
        private const string TempDataKey = "Maliev.LeadAnalyticsEvent";

        /// <summary>
        /// Attempts to queue a persisted contact-message event without allowing analytics failures to escape.
        /// </summary>
        /// <param name="tempData">The request TempData dictionary.</param>
        /// <param name="messageId">The persisted message identifier.</param>
        /// <param name="failure">The validation, serialization, or TempData failure, when unsuccessful.</param>
        /// <returns><see langword="true" /> when the event was queued.</returns>
        internal static bool TryQueueContactMessage(ITempDataDictionary tempData, int messageId, out Exception? failure)
        {
            return TryQueue(tempData, "contact", "general_contact", "message", messageId, false, out failure);
        }

        /// <summary>
        /// Attempts to queue a persisted manual quotation event without allowing analytics failures to escape.
        /// </summary>
        /// <param name="tempData">The request TempData dictionary.</param>
        /// <param name="requestId">The persisted quotation-request identifier.</param>
        /// <param name="hasFiles">Whether the submitted request included files.</param>
        /// <param name="failure">The validation, serialization, or TempData failure, when unsuccessful.</param>
        /// <returns><see langword="true" /> when the event was queued.</returns>
        internal static bool TryQueueManualQuotation(
            ITempDataDictionary tempData,
            int requestId,
            bool hasFiles,
            out Exception? failure)
        {
            return TryQueue(tempData, "manual_quote", "custom_manufacturing", "quotation", requestId, hasFiles, out failure);
        }

        /// <summary>
        /// Attempts to queue a persisted instant 3D quotation event without allowing analytics failures to escape.
        /// </summary>
        /// <param name="tempData">The request TempData dictionary.</param>
        /// <param name="requestId">The persisted quotation-request identifier.</param>
        /// <param name="hasFiles">Whether the submitted request included files.</param>
        /// <param name="failure">The validation, serialization, or TempData failure, when unsuccessful.</param>
        /// <returns><see langword="true" /> when the event was queued.</returns>
        internal static bool TryQueueInstantQuotation(
            ITempDataDictionary tempData,
            int requestId,
            bool hasFiles,
            out Exception? failure)
        {
            return TryQueue(tempData, "instant_3d_quote", "3d_printing", "quotation", requestId, hasFiles, out failure);
        }

        /// <summary>
        /// Removes and returns one valid event from TempData.
        /// </summary>
        /// <param name="tempData">The request TempData dictionary.</param>
        /// <param name="leadEvent">The validated event, when present.</param>
        /// <returns><see langword="true" /> when a valid event was consumed.</returns>
        public static bool TryConsume(ITempDataDictionary tempData, out LeadAnalyticsEvent? leadEvent)
        {
            ArgumentNullException.ThrowIfNull(tempData);

            leadEvent = null;
            if (!tempData.TryGetValue(TempDataKey, out object? value))
            {
                return false;
            }

            tempData.Remove(TempDataKey);
            if (value is not string serializedEvent)
            {
                return false;
            }

            try
            {
                LeadAnalyticsEvent? candidate = JsonSerializer.Deserialize<LeadAnalyticsEvent>(serializedEvent);
                if (candidate == null || !candidate.IsAllowed())
                {
                    return false;
                }

                leadEvent = candidate;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void Queue(ITempDataDictionary tempData, LeadAnalyticsEvent leadEvent)
        {
            ArgumentNullException.ThrowIfNull(tempData);
            tempData[TempDataKey] = JsonSerializer.Serialize(leadEvent);
        }

        private static bool TryQueue(
            ITempDataDictionary tempData,
            string leadType,
            string service,
            string transactionPrefix,
            int persistedId,
            bool hasFiles,
            out Exception? failure)
        {
            try
            {
                Queue(
                    tempData,
                    new LeadAnalyticsEvent(
                        leadType,
                        service,
                        CreateTransactionId(transactionPrefix, persistedId),
                        hasFiles));
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
        }

        private static string CreateTransactionId(string prefix, int persistedId)
        {
            if (persistedId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(persistedId), "A persisted lead identifier must be positive.");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{persistedId}");
        }
    }

    /// <summary>
    /// Defines the complete non-PII wire shape exposed to the data layer.
    /// </summary>
    public sealed class LeadAnalyticsEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LeadAnalyticsEvent" /> class.
        /// </summary>
        /// <param name="leadType">The controlled lead type.</param>
        /// <param name="service">The controlled MALIEV service.</param>
        /// <param name="transactionId">The persisted-record transaction identifier.</param>
        /// <param name="hasFiles">Whether the lead included files.</param>
        [JsonConstructor]
        public LeadAnalyticsEvent(string leadType, string service, string transactionId, bool hasFiles)
        {
            this.LeadType = leadType;
            this.Service = service;
            this.TransactionId = transactionId;
            this.HasFiles = hasFiles;
        }

        /// <summary>
        /// Gets the application-owned event name.
        /// </summary>
        [JsonPropertyName("event")]
        public string Event { get; } = "maliev_lead_submitted";

        /// <summary>
        /// Gets the controlled lead type.
        /// </summary>
        [JsonPropertyName("lead_type")]
        public string LeadType { get; }

        /// <summary>
        /// Gets the controlled MALIEV service.
        /// </summary>
        [JsonPropertyName("service")]
        public string Service { get; }

        /// <summary>
        /// Gets the persisted-record transaction identifier.
        /// </summary>
        [JsonPropertyName("transaction_id")]
        public string TransactionId { get; }

        /// <summary>
        /// Gets the controlled persistence status.
        /// </summary>
        [JsonPropertyName("lead_status")]
        public string LeadStatus { get; } = "persisted";

        /// <summary>
        /// Gets a value indicating whether the lead included files.
        /// </summary>
        [JsonPropertyName("has_files")]
        public bool HasFiles { get; }

        internal bool IsAllowed()
        {
            if (!string.Equals(this.Event, "maliev_lead_submitted", StringComparison.Ordinal)
                || !string.Equals(this.LeadStatus, "persisted", StringComparison.Ordinal))
            {
                return false;
            }

            return (string.Equals(this.LeadType, "contact", StringComparison.Ordinal)
                    && string.Equals(this.Service, "general_contact", StringComparison.Ordinal)
                    && HasPositiveId(this.TransactionId, "message"))
                || (string.Equals(this.LeadType, "manual_quote", StringComparison.Ordinal)
                    && string.Equals(this.Service, "custom_manufacturing", StringComparison.Ordinal)
                    && HasPositiveId(this.TransactionId, "quotation"))
                || (string.Equals(this.LeadType, "instant_3d_quote", StringComparison.Ordinal)
                    && string.Equals(this.Service, "3d_printing", StringComparison.Ordinal)
                    && HasPositiveId(this.TransactionId, "quotation"));
        }

        private static bool HasPositiveId(string transactionId, string prefix)
        {
            string expectedPrefix = prefix + "-";
            return transactionId != null
                && transactionId.StartsWith(expectedPrefix, StringComparison.Ordinal)
                && int.TryParse(transactionId.AsSpan(expectedPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int persistedId)
                && persistedId > 0;
        }
    }
}
