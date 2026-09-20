// <copyright file="AdditiveQuoteTickets.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using System;
    using System.Collections.Generic;

    /// <summary>Protected proof that the web server accepted a specific immutable upload.</summary>
    public sealed class AdditiveUploadReceiptPayload
    {
        /// <summary>Gets or sets the upload receipt schema version.</summary>
        public string SchemaVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the owning instant-quotation session.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the server-issued immutable upload identifier.</summary>
        public string UploadId { get; set; } = string.Empty;

        /// <summary>Gets or sets the normalized original file name.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Gets or sets the immutable storage path containing the upload id.</summary>
        public string StoragePath { get; set; } = string.Empty;

        /// <summary>Gets or sets the SHA-256 digest computed by the server from accepted bytes.</summary>
        public string ContentSha256 { get; set; } = string.Empty;

        /// <summary>Gets or sets the analysis contract revision associated with this upload.</summary>
        public string AnalysisRevision { get; set; } = string.Empty;

        /// <summary>Gets or sets when the receipt was issued.</summary>
        public DateTimeOffset IssuedAtUtc { get; set; }

        /// <summary>Gets or sets when the receipt expires.</summary>
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    /// <summary>Canonical, protected monetary result for one additive quotation line.</summary>
    public sealed class AdditiveLineQuotePayload
    {
        /// <summary>Gets or sets the ticket schema version.</summary>
        public string SchemaVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the pricing-policy version.</summary>
        public string PolicyVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the owning instant-quotation session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the uploaded file name.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Gets or sets the immutable server-issued upload identifier.</summary>
        public string UploadId { get; set; } = string.Empty;

        /// <summary>Gets or sets the immutable upload storage path.</summary>
        public string StoragePath { get; set; } = string.Empty;

        /// <summary>Gets or sets the server-computed content SHA-256.</summary>
        public string ContentSha256 { get; set; } = string.Empty;

        /// <summary>Gets or sets the physical-analysis contract revision.</summary>
        public string AnalysisRevision { get; set; } = string.Empty;

        /// <summary>Gets or sets the manufacturing profile version used for this estimate.</summary>
        public string ProfileVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets estimate confidence: validated, provisional, or unavailable.</summary>
        public string Confidence { get; set; } = string.Empty;

        /// <summary>Gets or sets the required downstream review state.</summary>
        public string ReviewState { get; set; } = string.Empty;

        /// <summary>Gets or sets the digest of the normalized geometry inputs.</summary>
        public string GeometryDigest { get; set; } = string.Empty;

        /// <summary>Gets or sets the canonical material key.</summary>
        public string MaterialKey { get; set; } = string.Empty;

        /// <summary>Gets or sets the canonical build preference.</summary>
        public BuildPreference BuildPreference { get; set; }

        /// <summary>Gets or sets the additive process.</summary>
        public PrintProcess Process { get; set; }

        /// <summary>Gets or sets the priced quantity.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets direct physical cost per unit before commercial policy.</summary>
        public decimal DirectCostPerUnitThb { get; set; }

        /// <summary>Gets or sets the canonical unit price in THB.</summary>
        public decimal UnitPriceThb { get; set; }

        /// <summary>Gets or sets the canonical line subtotal in THB.</summary>
        public decimal SubtotalThb { get; set; }

        /// <summary>Gets or sets the total line shipping weight in grams.</summary>
        public decimal WeightGrams { get; set; }

        /// <summary>Gets or sets the total line bounding volume in cubic centimetres.</summary>
        public decimal BoundingCm3 { get; set; }

        /// <summary>Gets or sets print time per unit in minutes.</summary>
        public decimal PrintTimeMinutes { get; set; }

        /// <summary>Gets or sets material use per unit.</summary>
        public decimal MaterialPerUnit { get; set; }

        /// <summary>Gets or sets the effective display currency.</summary>
        public string EffectiveCurrency { get; set; } = string.Empty;

        /// <summary>Gets or sets the THB-to-display-currency multiplier.</summary>
        public decimal ExchangeRate { get; set; }

        /// <summary>Gets or sets when the ticket was issued.</summary>
        public DateTimeOffset IssuedAtUtc { get; set; }

        /// <summary>Gets or sets when the ticket expires.</summary>
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    /// <summary>Canonical protected result for an additive order total.</summary>
    public sealed class AdditiveOrderQuotePayload
    {
        /// <summary>Gets or sets the ticket schema version.</summary>
        public string SchemaVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the pricing-policy version.</summary>
        public string PolicyVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the owning session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets ordered digests of the exact line tickets included.</summary>
        public List<string> LineTicketDigests { get; set; } = new List<string>();

        /// <summary>Gets or sets line totals allocated in the same order as line-ticket digests.</summary>
        public List<decimal> AllocatedLineTotalsThb { get; set; } = new List<decimal>();

        /// <summary>Gets or sets the canonical item subtotal in THB.</summary>
        public decimal ItemsSubtotalThb { get; set; }

        /// <summary>Gets or sets printing after the minimum-order rule, in THB.</summary>
        public decimal PrintingThb { get; set; }

        /// <summary>Gets or sets the minimum-order price in THB.</summary>
        public decimal MinimumOrderPriceThb { get; set; }

        /// <summary>Gets or sets the minimum-order surcharge in THB.</summary>
        public decimal MinimumOrderSurchargeThb { get; set; }

        /// <summary>Gets or sets once-per-order setup/admin cost in THB.</summary>
        public decimal SetupThb { get; set; }

        /// <summary>Gets or sets the commercial reprint reserve in THB.</summary>
        public decimal ReserveThb { get; set; }

        /// <summary>Gets or sets packaging charged outside delivery in THB.</summary>
        public decimal PackagingThb { get; set; }

        /// <summary>Gets or sets payment fee gross-up in THB.</summary>
        public decimal PaymentFeeThb { get; set; }

        /// <summary>Gets or sets final THB-five ceiling adjustment.</summary>
        public decimal RoundingAdjustmentThb { get; set; }

        /// <summary>Gets or sets shipping in THB.</summary>
        public decimal ShippingThb { get; set; }

        /// <summary>Gets or sets VAT in THB.</summary>
        public decimal VatThb { get; set; }

        /// <summary>Gets or sets the final estimated total in THB.</summary>
        public decimal FinalOrderPriceThb { get; set; }

        /// <summary>Gets or sets the capacity-derived lead-time lower bound in calendar days.</summary>
        public int LeadTimeMinimumDays { get; set; }

        /// <summary>Gets or sets the buffered lead-time upper bound in calendar days.</summary>
        public int LeadTimeMaximumDays { get; set; }

        /// <summary>Gets or sets the effective display currency.</summary>
        public string EffectiveCurrency { get; set; } = string.Empty;

        /// <summary>Gets or sets the THB-to-display-currency multiplier.</summary>
        public decimal ExchangeRate { get; set; }

        /// <summary>Gets or sets the normalized destination country code.</summary>
        public string DestinationCountryCode { get; set; } = string.Empty;

        /// <summary>Gets or sets the shipping pricing state.</summary>
        public string ShippingState { get; set; } = string.Empty;

        /// <summary>Gets or sets when the ticket was issued.</summary>
        public DateTimeOffset IssuedAtUtc { get; set; }

        /// <summary>Gets or sets when the ticket expires.</summary>
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    /// <summary>Represents a rejected, expired, or mismatched protected quote.</summary>
    public sealed class AdditiveQuoteTicketException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="AdditiveQuoteTicketException"/> class.</summary>
        /// <param name="code">Stable client-facing failure code.</param>
        /// <param name="message">Diagnostic message.</param>
        /// <param name="innerException">Underlying protection or serialization failure.</param>
        public AdditiveQuoteTicketException(string code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            this.Code = code;
        }

        /// <summary>Gets the stable failure code.</summary>
        public string Code { get; }
    }
}


