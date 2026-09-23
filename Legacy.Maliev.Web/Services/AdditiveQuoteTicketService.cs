// <copyright file="AdditiveQuoteTicketService.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using Legacy.Maliev.Web.Application;
    using Microsoft.AspNetCore.DataProtection;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    /// <summary>Protects and validates short-lived server-authoritative additive quote payloads.</summary>
    public sealed class AdditiveQuoteTicketService : IInstantQuotationQuoteTicketService
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

        /// <inheritdoc />
        public InstantQuotationQuoteAuthorization Issue(
            InstantQuotationSessionState session,
            InstantQuotationOrderQuote quote,
            DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(quote);
            if (session.Parts.Count == 0 || session.Parts.Count != quote.Parts.Count)
            {
                throw new ArgumentException("The authoritative quotation does not match the protected session.", nameof(quote));
            }

            if (quote.Parts.Any(part => part.Quantity < 1
                || Convert.ToDecimal(part.Subtotal) != Convert.ToDecimal(part.UnitPrice) * part.Quantity)
                || Convert.ToDecimal(quote.ItemsSubtotal)
                != quote.Parts.Sum(part => Convert.ToDecimal(part.Subtotal)))
            {
                throw new ArgumentException("The order subtotal must equal the protected rounded line subtotals.", nameof(quote));
            }

            var expiresAt = now.Add(TicketLifetime);
            var lineTickets = session.Parts.Select((part, index) =>
            {
                InstantQuotationPartQuote line = quote.Parts[index];
                if (part.PartId != line.PartId)
                {
                    throw new ArgumentException("The authoritative quotation line order is inconsistent.", nameof(quote));
                }

                return this.ProtectLine(new AdditiveLineQuotePayload
                {
                    SchemaVersion = LineSchemaVersion,
                    PolicyVersion = PricingCatalog.AdditivePricingPolicyVersion,
                    SessionId = session.SessionId,
                    FileName = part.DisplayFileName,
                    UploadId = part.UploadReference.Value,
                    StoragePath = NormalizePath(part.UploadReference.Value),
                    ContentSha256 = part.Geometry.Sha256,
                    AnalysisRevision = part.Geometry.ClaimVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ProfileVersion = PricingCatalog.AdditivePricingPolicyVersion,
                    Confidence = "provisional",
                    ReviewState = "engineer_review_required",
                    GeometryDigest = CreateGeometryDigest(part.Geometry),
                    MaterialKey = line.MaterialKey,
                    BuildPreference = line.BuildPreference,
                    Process = line.Process,
                    Quantity = line.Quantity,
                    DirectCostPerUnitThb = Convert.ToDecimal(line.DirectCostPerUnit),
                    UnitPriceThb = Convert.ToDecimal(line.UnitPrice),
                    SubtotalThb = Convert.ToDecimal(line.Subtotal),
                    WeightGrams = Convert.ToDecimal(line.WeightGramsPerUnit * line.Quantity),
                    BoundingCm3 = Convert.ToDecimal(line.BoundingCm3PerUnit * line.Quantity),
                    PrintTimeMinutes = Convert.ToDecimal(line.PrintTimeMinutesPerUnit),
                    MaterialPerUnit = Convert.ToDecimal(line.MaterialPerUnit),
                    EffectiveCurrency = "THB",
                    ExchangeRate = 1m,
                    IssuedAtUtc = now,
                    ExpiresAtUtc = expiresAt,
                });
            }).ToArray();
            var allocations = quote.AllocatedLineTotals?.Select(Convert.ToDecimal).ToList()
                ?? throw new ArgumentException("The authoritative quotation requires deterministic line allocations.", nameof(quote));
            var orderTicket = this.ProtectOrder(new AdditiveOrderQuotePayload
            {
                SchemaVersion = OrderSchemaVersion,
                PolicyVersion = PricingCatalog.AdditivePricingPolicyVersion,
                SessionId = session.SessionId,
                LineTicketDigests = lineTickets.Select(DigestTicket).ToList(),
                AllocatedLineTotalsThb = allocations,
                ItemsSubtotalThb = Convert.ToDecimal(quote.ItemsSubtotal),
                PrintingThb = Convert.ToDecimal(quote.Printing),
                MinimumOrderPriceThb = Convert.ToDecimal(quote.MinimumOrderPrice),
                MinimumOrderSurchargeThb = Convert.ToDecimal(quote.MinimumOrderSurcharge),
                SetupThb = Convert.ToDecimal(quote.Setup),
                ReserveThb = Convert.ToDecimal(quote.Reserve),
                PackagingThb = Convert.ToDecimal(quote.Packaging),
                PaymentFeeThb = Convert.ToDecimal(quote.PaymentFee),
                RoundingAdjustmentThb = Convert.ToDecimal(quote.RoundingAdjustment),
                ShippingThb = Convert.ToDecimal(quote.ShippingCost),
                VatThb = Convert.ToDecimal(quote.Vat),
                FinalOrderPriceThb = Convert.ToDecimal(quote.FinalOrderPrice),
                LeadTimeMinimumDays = quote.LeadTimeMinimumDays,
                LeadTimeMaximumDays = quote.LeadTimeMaximumDays,
                EffectiveCurrency = "THB",
                ExchangeRate = 1m,
                DestinationCountryCode = quote.DestinationCountryCode,
                ShippingState = quote.ShippingState.ToString(),
                IssuedAtUtc = now,
                ExpiresAtUtc = expiresAt,
            });
            return new InstantQuotationQuoteAuthorization(lineTickets, orderTicket);
        }

        /// <inheritdoc />
        public bool Validate(
            InstantQuotationSessionState session,
            InstantQuotationOrderQuote quote,
            InstantQuotationQuoteAuthorization authorization,
            DateTimeOffset now)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(session);
                ArgumentNullException.ThrowIfNull(quote);
                ArgumentNullException.ThrowIfNull(authorization);
                if (authorization.LineTickets.Count != session.Parts.Count
                    || authorization.LineTickets.Count != quote.Parts.Count)
                {
                    return false;
                }

                var order = this.UnprotectOrder(authorization.OrderTicket, now);
                if (!string.Equals(order.SessionId, session.SessionId, StringComparison.Ordinal)
                    || !this.MatchesLineTickets(order, authorization.LineTickets)
                    || order.FinalOrderPriceThb != Convert.ToDecimal(quote.FinalOrderPrice)
                    || order.ItemsSubtotalThb != Convert.ToDecimal(quote.ItemsSubtotal)
                    || order.ShippingThb != Convert.ToDecimal(quote.ShippingCost)
                    || order.LeadTimeMinimumDays != quote.LeadTimeMinimumDays
                    || order.LeadTimeMaximumDays != quote.LeadTimeMaximumDays)
                {
                    return false;
                }

                for (var index = 0; index < authorization.LineTickets.Count; index++)
                {
                    AdditiveLineQuotePayload payload = this.UnprotectLine(authorization.LineTickets[index], now);
                    InstantQuotationPart part = session.Parts[index];
                    InstantQuotationPartQuote line = quote.Parts[index];
                    if (part.PartId != line.PartId
                        || !this.MatchesLineIdentity(
                            payload,
                            session.SessionId,
                            part.DisplayFileName,
                            line.MaterialKey,
                            line.BuildPreference,
                            line.Quantity,
                            part.UploadReference.Value)
                        || !string.Equals(payload.ContentSha256, part.Geometry.Sha256, StringComparison.Ordinal)
                        || !string.Equals(payload.GeometryDigest, CreateGeometryDigest(part.Geometry), StringComparison.Ordinal)
                        || payload.DirectCostPerUnitThb != Convert.ToDecimal(line.DirectCostPerUnit)
                        || payload.UnitPriceThb != Convert.ToDecimal(line.UnitPrice)
                        || payload.SubtotalThb != Convert.ToDecimal(line.Subtotal))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception) when (exception is AdditiveQuoteTicketException or ArgumentException or InvalidOperationException)
            {
                return false;
            }
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

        private static string NormalizePath(string value) => value.Replace('\\', '/').Trim('/');

        private static string CreateGeometryDigest(AuthoritativeInstantQuotationGeometry geometry)
        {
            string canonical = FormattableString.Invariant(
                $"{geometry.Sha256}|{geometry.DimensionXmm:R}|{geometry.DimensionYmm:R}|{geometry.DimensionZmm:R}|{geometry.VolumeMm3:R}|{geometry.SurfaceAreaMm2:R}|{geometry.FacetCount}|{geometry.BodyCount}|{geometry.MinThicknessMm:R}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
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
