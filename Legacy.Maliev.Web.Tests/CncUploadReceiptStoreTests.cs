// <copyright file="CncUploadReceiptStoreTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
    using Xunit;

    /// <summary>
    /// Proves the local CNC receipt store's atomic, bounded, and expiring contract.
    /// </summary>
    public class CncUploadReceiptStoreTests
    {
        [Fact]
        public void LocalStore_AdvertisesThatItIsNotProductionDistributed()
        {
            var store = new InMemoryCncUploadReceiptStore();

            Assert.False(store.IsSharedDistributedAtomic);
        }

        [Fact]
        public void Reserve_FinalizeRollbackAndCapAreAtomicBeforeObjectCreation()
        {
            var store = new InMemoryCncUploadReceiptStore();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Assert.True(store.TryReserve(State("form-a", "item-1", "model", "r1", now.AddMinutes(1)), now, 2, out CncUploadReceiptReservation? first));
            Assert.NotNull(first);
            store.Finalize(first, now);
            Assert.True(store.TryReserve(State("form-a", "item-1", "model", "r2", now.AddMinutes(1)), now, 2, out CncUploadReceiptReservation? replacement));
            Assert.False(store.TryReserve(State("form-a", "item-1", "model", "r-concurrent", now.AddMinutes(1)), now, 2, out _));
            Assert.NotNull(replacement);
            store.Rollback(replacement, now);
            Assert.True(store.TryReserve(State("form-a", "item-1", "drawing", "r3", now.AddMinutes(1)), now, 2, out CncUploadReceiptReservation? drawing));
            Assert.NotNull(drawing);
            store.Finalize(drawing, now);
            Assert.False(store.TryReserve(State("form-a", "item-2", "model", "r4", now.AddMinutes(1)), now, 2, out _));

            Assert.True(store.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-a", "session-a", "item-1", "model", "r1") },
                now,
                out _));

            Assert.True(store.TryReserve(State("form-a", "item-2", "model", "r4", now.AddMinutes(2)), now.AddMinutes(1), 2, out _));
        }

        [Fact]
        public async Task ClaimAll_IsAtomicAndOnlyOneConcurrentSubmissionCanWin()
        {
            var store = new InMemoryCncUploadReceiptStore();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Issue(store, State("form-a", "item-1", "model", "model-r", now.AddHours(1)), now);
            Issue(store, State("form-a", "item-1", "drawing", "drawing-r", now.AddHours(1)), now);
            CncUploadReceiptClaim[] claims =
            {
                new CncUploadReceiptClaim("form-a", "session-a", "item-1", "model", "model-r"),
                new CncUploadReceiptClaim("form-a", "session-a", "item-1", "drawing", "drawing-r"),
            };

            Task<(bool Won, CncUploadReceiptClaimSet? Set)>[] attempts = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() =>
                {
                    bool won = store.TryClaimAll(claims, now, out CncUploadReceiptClaimSet? set);
                    return (won, set);
                }))
                .ToArray();

            (bool Won, CncUploadReceiptClaimSet? Set)[] results = await Task.WhenAll(attempts);
            Assert.Single(results, result => result.Won);
            Assert.Single(results, result => !result.Won);
            var winningSet = results.Single(result => result.Won).Set;
            Assert.NotNull(winningSet);
            Assert.Equal(2, winningSet.Receipts.Count);
        }

        [Fact]
        public void Restore_DoesNotOverwriteANewerReplacementAndRejectsExpiredClaims()
        {
            var store = new InMemoryCncUploadReceiptStore();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Issue(store, State("form-a", "item-1", "model", "old", now.AddMinutes(1)), now);
            Assert.True(store.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-a", "session-a", "item-1", "model", "old") },
                now,
                out CncUploadReceiptClaimSet? claimed));
            Issue(store, State("form-a", "item-1", "model", "new", now.AddMinutes(2)), now);

            Assert.NotNull(claimed);
            store.Restore(claimed, now);

            Assert.False(store.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-a", "session-a", "item-1", "model", "old") },
                now,
                out _));
            Assert.True(store.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-a", "session-a", "item-1", "model", "new") },
                now,
                out _));

            var expiredStore = new InMemoryCncUploadReceiptStore();
            Issue(expiredStore, State("form-b", "item-1", "model", "expired", now.AddSeconds(1)), now);
            Assert.True(expiredStore.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-b", "session-a", "item-1", "model", "expired") },
                now,
                out CncUploadReceiptClaimSet? expiredClaim));
            Assert.NotNull(expiredClaim);
            expiredStore.Restore(expiredClaim, now.AddSeconds(2));
            Assert.False(expiredStore.TryClaimAll(
                new[] { new CncUploadReceiptClaim("form-b", "session-a", "item-1", "model", "expired") },
                now.AddSeconds(2),
                out _));
        }

        [Fact]
        public void PendingObjectCreation_DoesNotExpireAndBlocksARefreshedFormUntilConfirmedRollback()
        {
            var store = new InMemoryCncUploadReceiptStore();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Assert.True(store.TryReserve(State("form-a", "item-1", "model", "pending", now.AddSeconds(1)), now, 40, out CncUploadReceiptReservation? pending));

            Assert.False(store.TryReserve(State("form-b", "item-1", "model", "refreshed", now.AddHours(2)), now.AddHours(1), 40, out _));

            Assert.NotNull(pending);
            store.Rollback(pending, now.AddHours(1));
            Assert.True(store.TryReserve(State("form-b", "item-1", "model", "retry", now.AddHours(2)), now.AddHours(1), 40, out _));
        }

        private static CncUploadReceiptState State(string formId, string itemId, string role, string receipt, DateTimeOffset expires) =>
            new CncUploadReceiptState(formId, "session-a", itemId, role, receipt, expires);

        private static void Issue(InMemoryCncUploadReceiptStore store, CncUploadReceiptState state, DateTimeOffset now)
        {
            Assert.True(store.TryReserve(state, now, 40, out CncUploadReceiptReservation? reservation));
            Assert.NotNull(reservation);
            store.Finalize(reservation, now);
        }
    }
}
