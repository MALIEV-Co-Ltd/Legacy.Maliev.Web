// <copyright file="AdditiveQuoteTicketServiceTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application;
    using Legacy.Maliev.Web.Application.Pricing;
    using Microsoft.AspNetCore.DataProtection;
    using System;
    using System.Collections.Generic;
    using Xunit;

    /// <summary>Tests the additive quote ticket monetary trust boundary.</summary>
    public class AdditiveQuoteTicketServiceTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void LineTicket_RoundTripPreservesCanonicalMoneyAndIdentity()
        {
            AdditiveQuoteTicketService service = CreateService();
            AdditiveLineQuotePayload expected = LinePayload();

            string ticket = service.ProtectLine(expected);
            AdditiveLineQuotePayload actual = service.UnprotectLine(ticket, Now.AddMinutes(1));

            Assert.Equal("session-1", actual.SessionId);
            Assert.Equal("part.stl", actual.FileName);
            Assert.Equal("PLA", actual.MaterialKey);
            Assert.Equal(BuildPreference.Strength, actual.BuildPreference);
            Assert.Equal(520m, actual.UnitPriceThb);
            Assert.Equal(1040m, actual.SubtotalThb);
            Assert.Equal(PricingCatalog.AdditivePricingPolicyVersion, actual.PolicyVersion);
            Assert.Equal("upload-1", actual.UploadId);
            Assert.Equal(new string('A', 64), actual.ContentSha256);
            Assert.Equal("analysis-v1", actual.AnalysisRevision);
            Assert.Equal("2026-9-14/session-1/upload-1/part.stl", actual.StoragePath);
        }

        [Fact]
        public void UploadTicket_RoundTripBindsServerHashPathAndSession()
        {
            AdditiveQuoteTicketService service = CreateService();
            var expected = new AdditiveUploadReceiptPayload
            {
                SchemaVersion = AdditiveQuoteTicketService.UploadSchemaVersion,
                SessionId = "session-1",
                UploadId = "upload-1",
                FileName = "part.stl",
                StoragePath = "2026-9-14/session-1/upload-1/part.stl",
                ContentSha256 = new string('A', 64),
                AnalysisRevision = "analysis-v1",
                IssuedAtUtc = Now,
                ExpiresAtUtc = Now.AddMinutes(30),
            };

            string ticket = service.ProtectUpload(expected);
            AdditiveUploadReceiptPayload actual = service.UnprotectUpload(ticket, Now.AddMinutes(1));

            Assert.Equal(expected.UploadId, actual.UploadId);
            Assert.Equal(expected.ContentSha256, actual.ContentSha256);
            Assert.Equal(expected.StoragePath, actual.StoragePath);
            Assert.True(service.MatchesUploadIdentity(actual, "session-1", "part.stl"));
            Assert.False(service.MatchesUploadIdentity(actual, "session-2", "part.stl"));
            Assert.False(service.MatchesUploadIdentity(actual, "session-1", "other.stl"));
        }

        [Fact]
        public void UploadTicket_InvalidDigestIsRejected()
        {
            AdditiveQuoteTicketService service = CreateService();
            var payload = new AdditiveUploadReceiptPayload
            {
                SchemaVersion = AdditiveQuoteTicketService.UploadSchemaVersion,
                SessionId = "session-1",
                UploadId = "upload-1",
                FileName = "part.stl",
                StoragePath = "2026-9-14/session-1/upload-1/part.stl",
                ContentSha256 = "not-a-digest",
                AnalysisRevision = "analysis-v1",
                IssuedAtUtc = Now,
                ExpiresAtUtc = Now.AddMinutes(30),
            };

            string ticket = service.ProtectUpload(payload);

            AdditiveQuoteTicketException error = Assert.Throws<AdditiveQuoteTicketException>(
                () => service.UnprotectUpload(ticket, Now.AddMinutes(1)));
            Assert.Equal("upload_invalid", error.Code);
        }

        [Fact]
        public void LineTicket_ModifiedCiphertextIsRejected()
        {
            AdditiveQuoteTicketService service = CreateService();
            string ticket = service.ProtectLine(LinePayload());
            int tamperIndex = ticket.Length / 2;
            char replacement = ticket[tamperIndex] == 'A' ? 'B' : 'A';
            string tampered = ticket.Substring(0, tamperIndex) + replacement + ticket.Substring(tamperIndex + 1);

            AdditiveQuoteTicketException error = Assert.Throws<AdditiveQuoteTicketException>(
                () => service.UnprotectLine(tampered, Now));

            Assert.Equal("quote_invalid", error.Code);
        }

        [Fact]
        public void LineTicket_ExpiredPayloadIsRejected()
        {
            AdditiveQuoteTicketService service = CreateService();
            string ticket = service.ProtectLine(LinePayload());

            AdditiveQuoteTicketException error = Assert.Throws<AdditiveQuoteTicketException>(
                () => service.UnprotectLine(ticket, Now.AddMinutes(31)));

            Assert.Equal("quote_expired", error.Code);
        }

        [Fact]
        public void OrderTicket_BindsTheExactLineTicketSet()
        {
            AdditiveQuoteTicketService service = CreateService();
            string firstLine = service.ProtectLine(LinePayload());
            AdditiveLineQuotePayload secondPayload = LinePayload();
            secondPayload.FileName = "second.stl";
            string secondLine = service.ProtectLine(secondPayload);
            var payload = new AdditiveOrderQuotePayload
            {
                SchemaVersion = AdditiveQuoteTicketService.OrderSchemaVersion,
                PolicyVersion = PricingCatalog.AdditivePricingPolicyVersion,
                SessionId = "session-1",
                LineTicketDigests = new List<string>
                {
                    AdditiveQuoteTicketService.DigestTicket(firstLine),
                    AdditiveQuoteTicketService.DigestTicket(secondLine),
                },
                AllocatedLineTotalsThb = new List<decimal> { 1166.30m, 1166.30m },
                ItemsSubtotalThb = 2080m,
                PrintingThb = 2080m,
                ShippingThb = 100m,
                VatThb = 152.60m,
                FinalOrderPriceThb = 2332.60m,
                LeadTimeMinimumDays = 1,
                LeadTimeMaximumDays = 3,
                EffectiveCurrency = "THB",
                ExchangeRate = 1m,
                DestinationCountryCode = "TH",
                ShippingState = ShippingPricingState.DomesticPriced.ToString(),
                IssuedAtUtc = Now,
                ExpiresAtUtc = Now.AddMinutes(30),
            };

            string ticket = service.ProtectOrder(payload);
            AdditiveOrderQuotePayload actual = service.UnprotectOrder(ticket, Now.AddMinutes(1));

            Assert.True(service.MatchesLineTickets(actual, new[] { firstLine, secondLine }));
            Assert.False(service.MatchesLineTickets(actual, new[] { secondLine, firstLine }));
            Assert.False(service.MatchesLineTickets(actual, new[] { firstLine }));
        }

        [Fact]
        public void LineIdentity_RejectsCrossPartAndChangedSettings()
        {
            AdditiveQuoteTicketService service = CreateService();
            AdditiveLineQuotePayload payload = LinePayload();

            Assert.True(service.MatchesLineIdentity(payload, "session-1", "part.stl", "PLA", BuildPreference.Strength, 2));
            Assert.False(service.MatchesLineIdentity(payload, "session-2", "part.stl", "PLA", BuildPreference.Strength, 2));
            Assert.False(service.MatchesLineIdentity(payload, "session-1", "other.stl", "PLA", BuildPreference.Strength, 2));
            Assert.False(service.MatchesLineIdentity(payload, "session-1", "part.stl", "PETG", BuildPreference.Strength, 2));
            Assert.False(service.MatchesLineIdentity(payload, "session-1", "part.stl", "PLA", BuildPreference.Standard, 2));
            Assert.False(service.MatchesLineIdentity(payload, "session-1", "part.stl", "PLA", BuildPreference.Strength, 3));
            Assert.False(service.MatchesLineIdentity(payload, "session-1", "part.stl", "PLA", BuildPreference.Strength, 2, "other/path.stl"));
        }

        [Fact]
        public void OrderTicket_InternationalShippingCannotCarryDomesticAmount()
        {
            AdditiveQuoteTicketService service = CreateService();
            string line = service.ProtectLine(LinePayload());
            var payload = new AdditiveOrderQuotePayload
            {
                SchemaVersion = AdditiveQuoteTicketService.OrderSchemaVersion,
                PolicyVersion = PricingCatalog.AdditivePricingPolicyVersion,
                SessionId = "session-1",
                LineTicketDigests = new List<string> { AdditiveQuoteTicketService.DigestTicket(line) },
                AllocatedLineTotalsThb = new List<decimal> { 1219.80m },
                ItemsSubtotalThb = 1040m,
                PrintingThb = 1040m,
                ShippingThb = 100m,
                VatThb = 79.80m,
                FinalOrderPriceThb = 1219.80m,
                LeadTimeMinimumDays = 1,
                LeadTimeMaximumDays = 3,
                EffectiveCurrency = "THB",
                ExchangeRate = 1m,
                DestinationCountryCode = "US",
                ShippingState = ShippingPricingState.ToBeQuoted.ToString(),
                IssuedAtUtc = Now,
                ExpiresAtUtc = Now.AddMinutes(30),
            };

            string ticket = service.ProtectOrder(payload);

            Assert.Throws<AdditiveQuoteTicketException>(() => service.UnprotectOrder(ticket, Now));
        }

        [Fact]
        public void WorkflowAuthorization_RoundTripBindsSessionUploadSettingsAndMoney()
        {
            AdditiveQuoteTicketService service = CreateService();
            InstantQuotationSessionState session = Session();
            InstantQuotationOrderQuote quote = new InstantQuotationPricingService().Quote(session.RequestState);

            InstantQuotationQuoteAuthorization authorization = service.Issue(session, quote, Now);

            Assert.Single(authorization.LineTickets);
            Assert.True(service.Validate(session, quote, authorization, Now.AddMinutes(1)));
        }

        [Fact]
        public void WorkflowAuthorization_RejectsChangedSettingsAndTamperedOrderTicket()
        {
            AdditiveQuoteTicketService service = CreateService();
            InstantQuotationSessionState session = Session();
            InstantQuotationOrderQuote quote = new InstantQuotationPricingService().Quote(session.RequestState);
            InstantQuotationQuoteAuthorization authorization = service.Issue(session, quote, Now);
            InstantQuotationPart part = session.Parts[0];
            InstantQuotationSessionState changed = session with
            {
                RequestState = new InstantQuotationOrderState([
                    part with
                    {
                        Configuration = part.Configuration with { Quantity = part.Configuration.Quantity + 1 },
                    },
                ]),
            };
            InstantQuotationOrderQuote changedQuote = new InstantQuotationPricingService().Quote(changed.RequestState);
            string tampered = authorization.OrderTicket[..^1]
                + (authorization.OrderTicket[^1] == 'A' ? 'B' : 'A');

            Assert.False(service.Validate(changed, changedQuote, authorization, Now.AddMinutes(1)));
            Assert.False(service.Validate(
                session,
                quote,
                authorization with { OrderTicket = tampered },
                Now.AddMinutes(1)));
        }

        private static AdditiveQuoteTicketService CreateService()
        {
            return new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        }

        private static AdditiveLineQuotePayload LinePayload()
        {
            return new AdditiveLineQuotePayload
            {
                SchemaVersion = AdditiveQuoteTicketService.LineSchemaVersion,
                PolicyVersion = PricingCatalog.AdditivePricingPolicyVersion,
                SessionId = "session-1",
                FileName = "part.stl",
                UploadId = "upload-1",
                StoragePath = "2026-9-14/session-1/upload-1/part.stl",
                ContentSha256 = new string('A', 64),
                AnalysisRevision = "analysis-v1",
                ProfileVersion = "profile-v1",
                Confidence = "provisional",
                ReviewState = "engineer_review_required",
                GeometryDigest = "geometry-digest",
                MaterialKey = "PLA",
                BuildPreference = BuildPreference.Strength,
                Process = PrintProcess.Fdm,
                Quantity = 2,
                DirectCostPerUnitThb = 200m,
                UnitPriceThb = 520m,
                SubtotalThb = 1040m,
                WeightGrams = 24.5m,
                BoundingCm3 = 80m,
                PrintTimeMinutes = 60m,
                MaterialPerUnit = 12.25m,
                EffectiveCurrency = "THB",
                ExchangeRate = 1m,
                IssuedAtUtc = Now,
                ExpiresAtUtc = Now.AddMinutes(30),
            };
        }

        private static InstantQuotationSessionState Session()
        {
            var claim = new InstantQuotationGeometryClaim(
                1,
                new string('a', 64),
                20,
                20,
                10,
                2_000,
                1_200,
                Enumerable.Repeat(200d, 64).ToArray(),
                Enumerable.Repeat(80d, 64).ToArray(),
                1_024,
                1,
                true,
                false,
                false,
                0.8);
            var upload = InstantQuotationUploadResult.Succeeded(
                "operation",
                new InstantQuotationUploadReference(Guid.NewGuid().ToString("D")),
                claim.Sha256);
            AuthoritativeInstantQuotationGeometry geometry =
                AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!;
            var part = new InstantQuotationPart(
                Guid.NewGuid(),
                "part.stl",
                upload.UploadReference!,
                geometry,
                new InstantQuotationPartConfiguration("PLA", "Black", 2, BuildPreference.Strength));
            return new InstantQuotationSessionState(
                "session-1",
                new string('b', 64),
                new InstantQuotationOrderState([part]),
                Now,
                Now);
        }
    }
}
