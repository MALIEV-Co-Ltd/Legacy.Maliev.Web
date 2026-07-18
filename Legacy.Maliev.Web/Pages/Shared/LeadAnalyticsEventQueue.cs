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
            return TryQueue(
                tempData,
                "contact_request",
                "general_contact",
                "message",
                messageId,
                false,
                false,
                out failure);
        }

        /// <summary>
        /// Attempts to queue a persisted manual quotation event without allowing analytics failures to escape.
        /// </summary>
        /// <param name="tempData">The request TempData dictionary.</param>
        /// <param name="requestId">The persisted quotation-request identifier.</param>
        /// <param name="service">The controlled MALIEV service context.</param>
        /// <param name="hasFiles">Whether the submitted request included files.</param>
        /// <param name="fileUploadCompleted">Whether every submitted file was stored and linked.</param>
        /// <param name="failure">The validation, serialization, or TempData failure, when unsuccessful.</param>
        /// <returns><see langword="true" /> when the event was queued.</returns>
        internal static bool TryQueueManualQuotation(
            ITempDataDictionary tempData,
            int requestId,
            string service,
            bool hasFiles,
            bool fileUploadCompleted,
            out Exception? failure)
        {
            return TryQueue(
                tempData,
                "quotation_request",
                service,
                "quotation",
                requestId,
                hasFiles,
                fileUploadCompleted,
                out failure);
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
            string intentType,
            string service,
            string transactionPrefix,
            int persistedId,
            bool hasFiles,
            bool fileUploadCompleted,
            out Exception? failure)
        {
            try
            {
                Queue(
                    tempData,
                    new LeadAnalyticsEvent(
                        intentType,
                        service,
                        CreateTransactionId(transactionPrefix, persistedId),
                        hasFiles,
                        fileUploadCompleted));
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
        /// <param name="intentType">The controlled request intent.</param>
        /// <param name="service">The controlled MALIEV service.</param>
        /// <param name="transactionId">The persisted-record transaction identifier.</param>
        /// <param name="hasFiles">Whether the lead included files.</param>
        /// <param name="fileUploadCompleted">Whether every submitted file was stored and linked.</param>
        [JsonConstructor]
        public LeadAnalyticsEvent(
            string intentType,
            string service,
            string transactionId,
            bool hasFiles,
            bool fileUploadCompleted)
        {
            this.IntentType = intentType;
            this.Service = service;
            this.TransactionId = transactionId;
            this.HasFiles = hasFiles;
            this.FileUploadCompleted = fileUploadCompleted;
        }

        /// <summary>
        /// Gets the application-owned event name.
        /// </summary>
        [JsonPropertyName("event")]
        public string Event { get; } = "request_quote";

        /// <summary>
        /// Gets the controlled lead type.
        /// </summary>
        [JsonPropertyName("intent_type")]
        public string IntentType { get; }

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
        [JsonPropertyName("submission_status")]
        public string SubmissionStatus { get; } = "persisted";

        /// <summary>
        /// Gets a value indicating whether the lead included files.
        /// </summary>
        [JsonPropertyName("has_files")]
        public bool HasFiles { get; }

        /// <summary>
        /// Gets a value indicating whether every submitted file was stored and linked.
        /// </summary>
        [JsonPropertyName("file_upload_completed")]
        public bool FileUploadCompleted { get; }

        internal bool IsAllowed()
        {
            if (!string.Equals(this.Event, "request_quote", StringComparison.Ordinal)
                || !string.Equals(this.SubmissionStatus, "persisted", StringComparison.Ordinal)
                || (this.FileUploadCompleted && !this.HasFiles))
            {
                return false;
            }

            return (string.Equals(this.IntentType, "contact_request", StringComparison.Ordinal)
                    && string.Equals(this.Service, "general_contact", StringComparison.Ordinal)
                    && !this.HasFiles
                    && HasPositiveId(this.TransactionId, "message"))
                || (string.Equals(this.IntentType, "quotation_request", StringComparison.Ordinal)
                    && IsQuotationService(this.Service)
                    && HasPositiveId(this.TransactionId, "quotation"));
        }

        private static bool IsQuotationService(string service) =>
            service is "3d_printing"
                or "3d_scanning"
                or "cnc_machining"
                or "injection_molding"
                or "custom_manufacturing";

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
