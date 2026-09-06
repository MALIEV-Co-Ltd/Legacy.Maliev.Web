// <copyright file="CncUploadReceiptStore.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Atomic ownership boundary for CNC upload receipts.
    /// </summary>
    public interface ICncUploadReceiptStore
    {
        /// <summary>
        /// Gets a value indicating whether claims are atomic across every application replica.
        /// A production implementation must return <see langword="true" /> only when its state is
        /// genuinely shared and its complete-set claim is atomic in that shared store.
        /// </summary>
        bool IsSharedDistributedAtomic { get; }

        /// <summary>
        /// Atomically reserves capacity for one form/item/role tuple before object creation.
        /// A finalized receipt for the same tuple remains claimable until this reservation is
        /// finalized, while a second concurrent reservation for the tuple is rejected. A pending
        /// object-creation reservation is an unresolved ownership tombstone: it must not expire
        /// until exact-path cleanup is confirmed and <see cref="Rollback" /> is called.
        /// </summary>
        bool TryReserve(CncUploadReceiptState receipt, DateTimeOffset now, int maximumOutstandingPerForm, out CncUploadReceiptReservation? reservation);

        /// <summary>
        /// Finalizes a live reservation after confirmed object creation. For a reservation
        /// returned by <see cref="TryReserve" />, this is an in-store transition which cannot
        /// reject because of the form cap.
        /// </summary>
        void Finalize(CncUploadReceiptReservation reservation, DateTimeOffset now);

        /// <summary>
        /// Rolls back a live reservation after a confirmed pre-upload or upload failure.
        /// Any previously finalized receipt for the same tuple remains available.
        /// </summary>
        void Rollback(CncUploadReceiptReservation reservation, DateTimeOffset now);

        /// <summary>
        /// Atomically verifies and removes the complete claimed receipt set.
        /// </summary>
        bool TryClaimAll(IReadOnlyCollection<CncUploadReceiptClaim> claims, DateTimeOffset now, out CncUploadReceiptClaimSet? claimSet);

        /// <summary>
        /// Restores an atomically claimed set only when request persistence was never attempted.
        /// A newer upload for the same tuple always wins.
        /// </summary>
        void Restore(CncUploadReceiptClaimSet claimSet, DateTimeOffset now);
    }

    /// <summary>
    /// One issued CNC upload receipt state entry.
    /// </summary>
    public sealed class CncUploadReceiptState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CncUploadReceiptState" /> class.
        /// </summary>
        public CncUploadReceiptState(string formId, string sessionId, string itemId, string role, string protectedReceipt, DateTimeOffset expiresAtUtc)
        {
            this.FormId = formId;
            this.SessionId = sessionId;
            this.ItemId = itemId;
            this.Role = role;
            this.ProtectedReceipt = protectedReceipt;
            this.ExpiresAtUtc = expiresAtUtc;
        }

        /// <summary>Gets the protected quotation form identifier.</summary>
        public string FormId { get; }

        /// <summary>Gets the protected browser journey/session identifier.</summary>
        public string SessionId { get; }

        /// <summary>Gets the indexed item identifier.</summary>
        public string ItemId { get; }

        /// <summary>Gets the upload role.</summary>
        public string Role { get; }

        /// <summary>Gets the exact protected receipt.</summary>
        public string ProtectedReceipt { get; }

        /// <summary>Gets the receipt expiry.</summary>
        public DateTimeOffset ExpiresAtUtc { get; }
    }

    /// <summary>
    /// Exact receipt value a submission wants to claim.
    /// </summary>
    public sealed class CncUploadReceiptClaim
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CncUploadReceiptClaim" /> class.
        /// </summary>
        public CncUploadReceiptClaim(string formId, string sessionId, string itemId, string role, string protectedReceipt)
        {
            this.FormId = formId;
            this.SessionId = sessionId;
            this.ItemId = itemId;
            this.Role = role;
            this.ProtectedReceipt = protectedReceipt;
        }

        /// <summary>Gets the protected quotation form identifier.</summary>
        public string FormId { get; }

        /// <summary>Gets the protected browser journey/session identifier.</summary>
        public string SessionId { get; }

        /// <summary>Gets the indexed item identifier.</summary>
        public string ItemId { get; }

        /// <summary>Gets the upload role.</summary>
        public string Role { get; }

        /// <summary>Gets the exact protected receipt being claimed.</summary>
        public string ProtectedReceipt { get; }
    }

    /// <summary>
    /// Opaque ownership token for one capacity reservation.
    /// </summary>
    public sealed class CncUploadReceiptReservation
    {
        /// <summary>Initializes a new reservation token.</summary>
        public CncUploadReceiptReservation(CncUploadReceiptState receipt, string token)
        {
            this.Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        /// <summary>Gets the receipt which will become active on finalization.</summary>
        public CncUploadReceiptState Receipt { get; }

        /// <summary>Gets the unguessable reservation token.</summary>
        public string Token { get; }
    }

    /// <summary>
    /// A complete atomically claimed set which may be restored before persistence begins.
    /// </summary>
    public sealed class CncUploadReceiptClaimSet
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CncUploadReceiptClaimSet" /> class.
        /// </summary>
        public CncUploadReceiptClaimSet(IReadOnlyList<CncUploadReceiptState> receipts) =>
            this.Receipts = receipts ?? throw new ArgumentNullException(nameof(receipts));

        /// <summary>Gets the states removed by the atomic claim.</summary>
        public IReadOnlyList<CncUploadReceiptState> Receipts { get; }
    }

    /// <summary>
    /// Process-local atomic CNC receipt store for Development and local testing only.
    /// It deliberately does not advertise distributed capability and therefore cannot enable
    /// the CNC route in Production.
    /// </summary>
    internal sealed class InMemoryCncUploadReceiptStore : ICncUploadReceiptStore
    {
        private readonly object sync = new object();

        private readonly Dictionary<string, ReceiptEntry> receipts =
            new Dictionary<string, ReceiptEntry>(StringComparer.Ordinal);

        public bool IsSharedDistributedAtomic => false;

        public bool TryReserve(CncUploadReceiptState receipt, DateTimeOffset now, int maximumOutstandingPerForm, out CncUploadReceiptReservation? reservation)
        {
            reservation = null;
            if (!IsValid(receipt) || maximumOutstandingPerForm <= 0 || receipt.ExpiresAtUtc <= now)
            {
                return false;
            }

            lock (this.sync)
            {
                this.CleanupExpired(now);
                string key = Key(receipt.FormId, receipt.SessionId, receipt.ItemId, receipt.Role);
                if (this.receipts.Values.Any(value =>
                        value.Pending != null
                        && string.Equals(value.Pending.Receipt.SessionId, receipt.SessionId, StringComparison.Ordinal)
                        && string.Equals(value.Pending.Receipt.ItemId, receipt.ItemId, StringComparison.Ordinal)
                        && string.Equals(value.Pending.Receipt.Role, receipt.Role, StringComparison.Ordinal))
                    || (this.receipts.TryGetValue(key, out ReceiptEntry? existing) && existing.Pending != null))
                {
                    return false;
                }

                if (existing == null
                    && this.receipts.Values.Count(value =>
                        string.Equals(value.Receipt.FormId, receipt.FormId, StringComparison.Ordinal)
                        && string.Equals(value.Receipt.SessionId, receipt.SessionId, StringComparison.Ordinal)) >= maximumOutstandingPerForm)
                {
                    return false;
                }

                var pending = new CncUploadReceiptReservation(receipt, Guid.NewGuid().ToString("N"));
                if (existing == null)
                {
                    existing = new ReceiptEntry();
                    this.receipts.Add(key, existing);
                }

                existing.Pending = pending;
                reservation = pending;
                return true;
            }
        }

        public void Finalize(CncUploadReceiptReservation reservation, DateTimeOffset now)
        {
            if (reservation == null)
            {
                throw new ArgumentNullException(nameof(reservation));
            }

            lock (this.sync)
            {
                this.CleanupExpired(now);
                string key = Key(reservation.Receipt.FormId, reservation.Receipt.SessionId, reservation.Receipt.ItemId, reservation.Receipt.Role);
                if (!this.receipts.TryGetValue(key, out ReceiptEntry? entry)
                    || entry.Pending == null
                    || !string.Equals(entry.Pending.Token, reservation.Token, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The CNC upload reservation is no longer live.");
                }

                entry.Active = reservation.Receipt;
                entry.Pending = null;
            }
        }

        public void Rollback(CncUploadReceiptReservation reservation, DateTimeOffset now)
        {
            if (reservation == null)
            {
                return;
            }

            lock (this.sync)
            {
                this.CleanupExpired(now);
                string key = Key(reservation.Receipt.FormId, reservation.Receipt.SessionId, reservation.Receipt.ItemId, reservation.Receipt.Role);
                if (this.receipts.TryGetValue(key, out ReceiptEntry? entry)
                    && entry.Pending != null
                    && string.Equals(entry.Pending.Token, reservation.Token, StringComparison.Ordinal))
                {
                    entry.Pending = null;
                    if (entry.Active == null)
                    {
                        this.receipts.Remove(key);
                    }
                }
            }
        }

        public bool TryClaimAll(IReadOnlyCollection<CncUploadReceiptClaim> claims, DateTimeOffset now, out CncUploadReceiptClaimSet? claimSet)
        {
            claimSet = null;
            if (claims == null || claims.Count == 0)
            {
                return false;
            }

            lock (this.sync)
            {
                this.CleanupExpired(now);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var matched = new List<CncUploadReceiptState>(claims.Count);
                foreach (CncUploadReceiptClaim claim in claims)
                {
                    if (claim == null
                        || string.IsNullOrWhiteSpace(claim.FormId)
                        || string.IsNullOrWhiteSpace(claim.SessionId)
                        || string.IsNullOrWhiteSpace(claim.ItemId)
                        || string.IsNullOrWhiteSpace(claim.Role)
                        || string.IsNullOrWhiteSpace(claim.ProtectedReceipt))
                    {
                        return false;
                    }

                    string key = Key(claim.FormId, claim.SessionId, claim.ItemId, claim.Role);
                    if (!keys.Add(key)
                        || !this.receipts.TryGetValue(key, out ReceiptEntry? entry)
                        || entry.Active == null
                        || (entry.Active.ExpiresAtUtc <= now)
                        || !string.Equals(entry.Active.ProtectedReceipt, claim.ProtectedReceipt, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    matched.Add(entry.Active);
                }

                foreach (string key in keys)
                {
                    ReceiptEntry entry = this.receipts[key];
                    entry.Active = null;
                    if (entry.Pending == null)
                    {
                        this.receipts.Remove(key);
                    }
                }

                claimSet = new CncUploadReceiptClaimSet(matched);
                return true;
            }
        }

        public void Restore(CncUploadReceiptClaimSet claimSet, DateTimeOffset now)
        {
            if (claimSet?.Receipts == null)
            {
                return;
            }

            lock (this.sync)
            {
                this.CleanupExpired(now);
                foreach (CncUploadReceiptState receipt in claimSet.Receipts)
                {
                    if (IsValid(receipt) && receipt.ExpiresAtUtc > now)
                    {
                        string key = Key(receipt.FormId, receipt.SessionId, receipt.ItemId, receipt.Role);
                        if (!this.receipts.TryGetValue(key, out ReceiptEntry? entry))
                        {
                            entry = new ReceiptEntry();
                            this.receipts.Add(key, entry);
                        }

                        if (entry.Active == null)
                        {
                            entry.Active = receipt;
                        }
                    }
                }
            }
        }

        internal void Clear()
        {
            lock (this.sync)
            {
                this.receipts.Clear();
            }
        }

        private static bool IsValid(CncUploadReceiptState receipt) =>
            receipt != null
            && !string.IsNullOrWhiteSpace(receipt.FormId)
            && !string.IsNullOrWhiteSpace(receipt.SessionId)
            && !string.IsNullOrWhiteSpace(receipt.ItemId)
            && !string.IsNullOrWhiteSpace(receipt.Role)
            && !string.IsNullOrWhiteSpace(receipt.ProtectedReceipt);

        private static string Key(string formId, string sessionId, string itemId, string role) =>
            formId + "\n" + sessionId + "\n" + itemId + "\n" + role;

        private void CleanupExpired(DateTimeOffset now)
        {
            foreach (KeyValuePair<string, ReceiptEntry> pair in this.receipts.ToArray())
            {
                if (pair.Value.Active?.ExpiresAtUtc <= now)
                {
                    pair.Value.Active = null;
                }

                // Pending means upstream object creation may have started. It is intentionally not
                // TTL-cleaned: only a confirmed exact-path delete or confirmed pre-object failure may
                // roll it back. This prevents a refresh/new form from bypassing ambiguous ownership.

                if (pair.Value.Active == null && pair.Value.Pending == null)
                {
                    this.receipts.Remove(pair.Key);
                }
            }
        }

        private sealed class ReceiptEntry
        {
            internal CncUploadReceiptState? Active { get; set; }

            internal CncUploadReceiptReservation? Pending { get; set; }

            internal CncUploadReceiptState Receipt => this.Pending?.Receipt ?? this.Active!;
        }
    }
}
