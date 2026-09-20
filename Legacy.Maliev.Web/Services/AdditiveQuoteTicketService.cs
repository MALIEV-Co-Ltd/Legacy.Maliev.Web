// <copyright file="AdditiveQuoteTicketService.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using Microsoft.AspNetCore.DataProtection;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    /// <summary>Protects and validates short-lived server-authoritative additive quote payloads.</summary>
    public sealed class AdditiveQuoteTicketService
    {
        /// <summary>Current line-ticket schema version.</summary>
        public const string LineSchemaVersion = "additive-line-quote.v2";

        /// <summary>Current upload-receipt schema version.</summary>
        public const string UploadSchemaVersion = "additive-upload-receipt.v1";

        /// <summary>Current order-ticket schema version.</summary>
        public const string OrderSchemaVersion = "additive-order-quote.v2";

        /// <summary>Lifetime shared by line and order tickets.</summary>
        public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(30);

        /// <summary>Lifetime of an accepted upload while a customer prepares a quote.</summary>
        public static readonly TimeSpan UploadTicketLifetime = TimeSpan.FromHours(24);

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly IDataProtector lineProtector;
        private readonly IDataProtector orderProtector;
        private readonly IDataProtector uploadProtector;

        /// <summary>Initializes a new instance of the <see cref="AdditiveQuoteTicketService"/> class.</summary>
        /// <param name="provider">Application Data Protection provider.</param>
        public AdditiveQuoteTicketService(IDataProtectionProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            this.lineProtector = provider.CreateProtector("Maliev.Web.AdditiveLineQuote.v2");
            this.orderProtector = provider.CreateProtector("Maliev.Web.AdditiveOrderQuote.v2");
            this.uploadProtector = provider.CreateProtector("Maliev.Web.AdditiveUploadReceipt.v1");
        }

        /// <summary>Protects a server-computed upload receipt.</summary>
        public string ProtectUpload(AdditiveUploadReceiptPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return this.uploadProtector.Protect(JsonSerializer.Serialize(payload, SerializerOptions));
        }

        /// <summary>Unprotects and validates a server-computed upload receipt.</summary>
        public AdditiveUploadReceiptPayload UnprotectUpload(string ticket, DateTimeOffset now)
        {
            AdditiveUploadReceiptPayload payload = this.Unprotect<AdditiveUploadReceiptPayload>(this.uploadProtector, ticket);
            ValidateUploadEnvelope(payload.SchemaVersion, payload.IssuedAtUtc, payload.ExpiresAtUtc, now);
            if (string.IsNullOrWhiteSpace(payload.SessionId)
                || string.IsNullOrWhiteSpace(payload.UploadId)
                || string.IsNullOrWhiteSpace(payload.FileName)
                || string.IsNullOrWhiteSpace(payload.StoragePath)
                || string.IsNullOrWhiteSpace(payload.AnalysisRevision)
                || !IsSha256(payload.ContentSha256))
            {
                throw new AdditiveQuoteTicketException("upload_invalid", "The upload receipt is incomplete or inconsistent.");
            }

            return payload;
        }

        /// <summary>Checks session ownership and normalized file identity.</summary>
        public bool MatchesUploadIdentity(AdditiveUploadReceiptPayload payload, string sessionId, string fileName)
        {
            return payload != null
                && string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(payload.FileName, fileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Protects one canonical line quote.</summary>
        /// <param name="payload">Canonical payload.</param>
        /// <returns>Opaque protected ticket.</returns>
        public string ProtectLine(AdditiveLineQuotePayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return this.lineProtector.Protect(JsonSerializer.Serialize(payload, SerializerOptions));
        }

        /// <summary>Unprotects and validates one line quote.</summary>
        /// <param name="ticket">Opaque protected ticket.</param>
        /// <param name="now">Current UTC time supplied by the caller.</param>
        /// <returns>Validated canonical payload.</returns>
        public AdditiveLineQuotePayload UnprotectLine(string ticket, DateTimeOffset now)
        {
            AdditiveLineQuotePayload payload = this.Unprotect<AdditiveLineQuotePayload>(this.lineProtector, ticket);
            ValidateEnvelope(payload.SchemaVersion, LineSchemaVersion, payload.PolicyVersion, payload.IssuedAtUtc, payload.ExpiresAtUtc, now);
            if (string.IsNullOrWhiteSpace(payload.SessionId)
                || string.IsNullOrWhiteSpace(payload.FileName)
                || string.IsNullOrWhiteSpace(payload.UploadId)
                || string.IsNullOrWhiteSpace(payload.StoragePath)
                || !IsSha256(payload.ContentSha256)
                || string.IsNullOrWhiteSpace(payload.AnalysisRevision)
                || string.IsNullOrWhiteSpace(payload.ProfileVersion)
                || !string.Equals(payload.Confidence, "provisional", StringComparison.Ordinal)
                || !string.Equals(payload.ReviewState, "engineer_review_required", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(payload.MaterialKey)
                || payload.Quantity <= 0
                || payload.DirectCostPerUnitThb <= 0
                || payload.UnitPriceThb < 0
                || payload.SubtotalThb != payload.UnitPriceThb * payload.Quantity)
            {
                throw new AdditiveQuoteTicketException("quote_invalid", "The line quote payload is incomplete or inconsistent.");
            }

            return payload;
        }

        /// <summary>Protects one canonical order quote.</summary>
        /// <param name="payload">Canonical payload.</param>
        /// <returns>Opaque protected ticket.</returns>
        public string ProtectOrder(AdditiveOrderQuotePayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return this.orderProtector.Protect(JsonSerializer.Serialize(payload, SerializerOptions));
        }

        /// <summary>Unprotects and validates one order quote.</summary>
        /// <param name="ticket">Opaque protected ticket.</param>
        /// <param name="now">Current UTC time supplied by the caller.</param>
        /// <returns>Validated canonical payload.</returns>
        public AdditiveOrderQuotePayload UnprotectOrder(string ticket, DateTimeOffset now)
        {
            AdditiveOrderQuotePayload payload = this.Unprotect<AdditiveOrderQuotePayload>(this.orderProtector, ticket);
            ValidateEnvelope(payload.SchemaVersion, OrderSchemaVersion, payload.PolicyVersion, payload.IssuedAtUtc, payload.ExpiresAtUtc, now);
            if (string.IsNullOrWhiteSpace(payload.SessionId)
                || payload.LineTicketDigests == null
                || payload.LineTicketDigests.Count == 0
                || payload.AllocatedLineTotalsThb == null
                || payload.AllocatedLineTotalsThb.Count != payload.LineTicketDigests.Count
                || payload.AllocatedLineTotalsThb.Any(total => total < 0m)
                || payload.AllocatedLineTotalsThb.Sum() != payload.FinalOrderPriceThb
                || payload.ItemsSubtotalThb < 0
                || payload.FinalOrderPriceThb < 0
                || payload.LeadTimeMinimumDays < 1
                || payload.LeadTimeMaximumDays < payload.LeadTimeMinimumDays
                || !Enum.TryParse(payload.ShippingState, out ShippingPricingState shippingState)
                || (shippingState == ShippingPricingState.ToBeQuoted && payload.ShippingThb != 0m))
            {
                throw new AdditiveQuoteTicketException("quote_invalid", "The order quote payload is incomplete.");
            }

            return payload;
        }

        /// <summary>Determines whether an order payload binds exactly to the supplied line tickets in order.</summary>
        /// <param name="payload">Unprotected order payload.</param>
        /// <param name="lineTickets">Submitted line tickets.</param>
        /// <returns><see langword="true"/> only for an exact ordered match.</returns>
        public bool MatchesLineTickets(AdditiveOrderQuotePayload payload, IReadOnlyList<string> lineTickets)
        {
            if (payload?.LineTicketDigests == null || lineTickets == null || payload.LineTicketDigests.Count != lineTickets.Count)
            {
                return false;
            }

            for (int index = 0; index < lineTickets.Count; index++)
            {
                if (!string.Equals(payload.LineTicketDigests[index], DigestTicket(lineTickets[index]), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Determines whether a protected line still belongs to the submitted part and settings.</summary>
        /// <param name="payload">Unprotected line payload.</param>
        /// <param name="sessionId">Current quotation session.</param>
        /// <param name="fileName">Submitted file name.</param>
        /// <param name="materialKey">Submitted material key.</param>
        /// <param name="buildPreference">Submitted build preference.</param>
        /// <param name="quantity">Submitted quantity.</param>
        /// <returns><see langword="true"/> only when every identity field matches.</returns>
        public bool MatchesLineIdentity(
            AdditiveLineQuotePayload payload,
            string sessionId,
            string fileName,
            string materialKey,
            BuildPreference buildPreference,
            int quantity)
        {
            return this.MatchesLineIdentity(payload, sessionId, fileName, materialKey, buildPreference, quantity, null);
        }

        /// <summary>Determines whether a protected line still belongs to the immutable upload and settings.</summary>
        public bool MatchesLineIdentity(
            AdditiveLineQuotePayload payload,
            string sessionId,
            string fileName,
            string materialKey,
            BuildPreference buildPreference,
            int quantity,
            string? storagePath)
        {
            return payload != null
                && string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(payload.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(payload.MaterialKey, materialKey, StringComparison.OrdinalIgnoreCase)
                && payload.BuildPreference == buildPreference
                && payload.Quantity == quantity
                && (string.IsNullOrWhiteSpace(storagePath)
                    || string.Equals(payload.StoragePath, storagePath.Replace('\\', '/').Trim('/'), StringComparison.Ordinal));
        }

        /// <summary>Creates a stable SHA-256 digest of an opaque ticket.</summary>
        /// <param name="ticket">Opaque ticket.</param>
        /// <returns>Uppercase hexadecimal digest.</returns>
        public static string DigestTicket(string ticket)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(ticket ?? string.Empty));
            return Convert.ToHexString(digest);
        }

        private static void ValidateEnvelope(
            string actualSchema,
            string expectedSchema,
            string policyVersion,
            DateTimeOffset issuedAtUtc,
            DateTimeOffset expiresAtUtc,
            DateTimeOffset now)
        {
            if (!string.Equals(actualSchema, expectedSchema, StringComparison.Ordinal)
                || !string.Equals(policyVersion, PricingCatalog.AdditivePricingPolicyVersion, StringComparison.Ordinal)
                || expiresAtUtc <= issuedAtUtc
                || issuedAtUtc > now.AddMinutes(5))
            {
                throw new AdditiveQuoteTicketException("quote_invalid", "The quote ticket schema or policy is invalid.");
            }

            if (now >= expiresAtUtc)
            {
                throw new AdditiveQuoteTicketException("quote_expired", "The quote ticket has expired.");
            }
        }

        private static void ValidateUploadEnvelope(
            string actualSchema,
            DateTimeOffset issuedAtUtc,
            DateTimeOffset expiresAtUtc,
            DateTimeOffset now)
        {
            if (!string.Equals(actualSchema, UploadSchemaVersion, StringComparison.Ordinal)
                || expiresAtUtc <= issuedAtUtc
                || issuedAtUtc > now.AddMinutes(5))
            {
                throw new AdditiveQuoteTicketException("upload_invalid", "The upload receipt schema or lifetime is invalid.");
            }

            if (now >= expiresAtUtc)
            {
                throw new AdditiveQuoteTicketException("upload_expired", "The upload receipt has expired.");
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private T Unprotect<T>(IDataProtector protector, string ticket)
            where T : class
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticket))
                {
                    throw new InvalidOperationException("A quote ticket is required.");
                }

                string json = protector.Unprotect(ticket);
                return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                    ?? throw new InvalidOperationException("The protected quote payload was empty.");
            }
            catch (AdditiveQuoteTicketException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AdditiveQuoteTicketException("quote_invalid", "The quote ticket could not be validated.", exception);
            }
        }
    }
}
