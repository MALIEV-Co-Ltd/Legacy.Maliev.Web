// <copyright file="RedisCncUploadReceiptStore.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using StackExchange.Redis;

    /// <summary>
    /// Redis-backed atomic ownership boundary for CNC upload receipts.
    /// </summary>
    internal sealed class RedisCncUploadReceiptStore(IConnectionMultiplexer redis) : ICncUploadReceiptStore
    {
        internal const string KeyPrefix = "legacy:web:cnc-receipts:";

        private const string ReserveScript = """
            local values = redis.call('HGETALL', KEYS[1])
            local count = 0
            for index = 1, #values, 2 do
              local field = values[index]
              local value = cjson.decode(values[index + 1])
              if value.a and tonumber(value.x) <= tonumber(ARGV[2]) then value.a = nil; value.x = nil end
              if value.p and value.i == ARGV[4] and value.r == ARGV[5] then return 0 end
              if value.f == ARGV[3] and (value.a or value.p) then count = count + 1 end
              if not value.a and not value.p then redis.call('HDEL', KEYS[1], field)
              else redis.call('HSET', KEYS[1], field, cjson.encode(value)) end
            end
            local current = redis.call('HGET', KEYS[1], ARGV[1])
            local value = current and cjson.decode(current) or { f = ARGV[3], i = ARGV[4], r = ARGV[5] }
            if value.p then return 0 end
            if not current and count >= tonumber(ARGV[6]) then return 0 end
            value.p = ARGV[7]; value.pr = ARGV[8]; value.px = ARGV[9]
            redis.call('HSET', KEYS[1], ARGV[1], cjson.encode(value))
            redis.call('PERSIST', KEYS[1])
            return 1
            """;

        private const string FinalizeScript = """
            local current = redis.call('HGET', KEYS[1], ARGV[1])
            if not current then return 0 end
            local value = cjson.decode(current)
            if value.p ~= ARGV[2] then return 0 end
            value.a = value.pr; value.x = value.px
            value.p = nil; value.pr = nil; value.px = nil
            redis.call('HSET', KEYS[1], ARGV[1], cjson.encode(value))
            local values = redis.call('HVALS', KEYS[1]); local maximum = 0
            for _, encoded in ipairs(values) do
              local entry = cjson.decode(encoded)
              if entry.p then redis.call('PERSIST', KEYS[1]); return 1 end
              if entry.x and tonumber(entry.x) > maximum then maximum = tonumber(entry.x) end
            end
            if maximum > 0 then redis.call('PEXPIREAT', KEYS[1], maximum) end
            return 1
            """;

        private const string RollbackScript = """
            local current = redis.call('HGET', KEYS[1], ARGV[1])
            if not current then return 0 end
            local value = cjson.decode(current)
            if value.p ~= ARGV[2] then return 0 end
            value.p = nil; value.pr = nil; value.px = nil
            if value.a then redis.call('HSET', KEYS[1], ARGV[1], cjson.encode(value))
            else redis.call('HDEL', KEYS[1], ARGV[1]) end
            local values = redis.call('HVALS', KEYS[1]); local maximum = 0
            for _, encoded in ipairs(values) do
              local entry = cjson.decode(encoded)
              if entry.p then redis.call('PERSIST', KEYS[1]); return 1 end
              if entry.x and tonumber(entry.x) > maximum then maximum = tonumber(entry.x) end
            end
            if maximum > 0 then redis.call('PEXPIREAT', KEYS[1], maximum) end
            return 1
            """;

        private const string ClaimScript = """
            local expiries = {}
            for index = 1, tonumber(ARGV[1]) do
              local offset = 3 + ((index - 1) * 2)
              local current = redis.call('HGET', KEYS[1], ARGV[offset])
              if not current then return {} end
              local value = cjson.decode(current)
              if not value.a or value.a ~= ARGV[offset + 1] or tonumber(value.x) <= tonumber(ARGV[2]) then return {} end
              expiries[index] = tostring(value.x)
            end
            for index = 1, tonumber(ARGV[1]) do
              local offset = 3 + ((index - 1) * 2)
              local current = redis.call('HGET', KEYS[1], ARGV[offset])
              local value = cjson.decode(current)
              value.a = nil; value.x = nil
              if value.p then redis.call('HSET', KEYS[1], ARGV[offset], cjson.encode(value))
              else redis.call('HDEL', KEYS[1], ARGV[offset]) end
            end
            local values = redis.call('HVALS', KEYS[1]); local maximum = 0
            for _, encoded in ipairs(values) do
              local entry = cjson.decode(encoded)
              if entry.p then redis.call('PERSIST', KEYS[1]); return expiries end
              if entry.x and tonumber(entry.x) > maximum then maximum = tonumber(entry.x) end
            end
            if maximum > 0 then redis.call('PEXPIREAT', KEYS[1], maximum) end
            return expiries
            """;

        private const string RestoreScript = """
            for index = 1, tonumber(ARGV[1]) do
              local offset = 3 + ((index - 1) * 6)
              if tonumber(ARGV[offset + 5]) > tonumber(ARGV[2]) then
                local current = redis.call('HGET', KEYS[1], ARGV[offset])
                local value = current and cjson.decode(current) or { f = ARGV[offset + 1], i = ARGV[offset + 2], r = ARGV[offset + 3] }
                if not value.a then value.a = ARGV[offset + 4]; value.x = ARGV[offset + 5] end
                redis.call('HSET', KEYS[1], ARGV[offset], cjson.encode(value))
              end
            end
            local values = redis.call('HVALS', KEYS[1]); local maximum = 0
            for _, encoded in ipairs(values) do
              local entry = cjson.decode(encoded)
              if entry.p then redis.call('PERSIST', KEYS[1]); return 1 end
              if entry.x and tonumber(entry.x) > maximum then maximum = tonumber(entry.x) end
            end
            if maximum > 0 then redis.call('PEXPIREAT', KEYS[1], maximum) end
            return 1
            """;

        private readonly IDatabase database = (redis ?? throw new ArgumentNullException(nameof(redis))).GetDatabase();

        public bool IsSharedDistributedAtomic => true;

        public bool TryReserve(CncUploadReceiptState receipt, DateTimeOffset now, int maximumOutstandingPerForm, out CncUploadReceiptReservation? reservation)
        {
            reservation = null;
            if (!IsValid(receipt) || maximumOutstandingPerForm <= 0 || receipt.ExpiresAtUtc <= now)
            {
                return false;
            }

            string token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var result = (long)this.database.ScriptEvaluate(
                ReserveScript,
                [Key(receipt.SessionId)],
                [Field(receipt.FormId, receipt.ItemId, receipt.Role), Milliseconds(now), Digest(receipt.FormId), Digest(receipt.ItemId),
                    receipt.Role, maximumOutstandingPerForm, token, receipt.ProtectedReceipt, Milliseconds(receipt.ExpiresAtUtc)]);
            if (result != 1)
            {
                return false;
            }

            reservation = new CncUploadReceiptReservation(receipt, token);
            return true;
        }

        public void Finalize(CncUploadReceiptReservation reservation, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(reservation);
            var result = (long)this.database.ScriptEvaluate(
                FinalizeScript,
                [Key(reservation.Receipt.SessionId)],
                [Field(reservation.Receipt.FormId, reservation.Receipt.ItemId, reservation.Receipt.Role), reservation.Token]);
            if (result != 1)
            {
                throw new InvalidOperationException("The CNC upload reservation is no longer live.");
            }
        }

        public void Rollback(CncUploadReceiptReservation reservation, DateTimeOffset now)
        {
            if (reservation is null)
            {
                return;
            }

            _ = this.database.ScriptEvaluate(
                RollbackScript,
                [Key(reservation.Receipt.SessionId)],
                [Field(reservation.Receipt.FormId, reservation.Receipt.ItemId, reservation.Receipt.Role), reservation.Token]);
        }

        public bool TryClaimAll(IReadOnlyCollection<CncUploadReceiptClaim> claims, DateTimeOffset now, out CncUploadReceiptClaimSet? claimSet)
        {
            claimSet = null;
            if (claims is null || claims.Count == 0 || claims.Any(static claim => !IsValid(claim)))
            {
                return false;
            }

            CncUploadReceiptClaim[] ordered = claims.ToArray();
            string sessionId = ordered[0].SessionId;
            if (ordered.Any(claim => !string.Equals(claim.SessionId, sessionId, StringComparison.Ordinal)))
            {
                return false;
            }

            var fields = new HashSet<string>(StringComparer.Ordinal);
            var arguments = new List<RedisValue>(2 + (ordered.Length * 2)) { ordered.Length, Milliseconds(now) };
            foreach (CncUploadReceiptClaim claim in ordered)
            {
                string field = Field(claim.FormId, claim.ItemId, claim.Role);
                if (!fields.Add(field))
                {
                    return false;
                }

                arguments.Add(field);
                arguments.Add(claim.ProtectedReceipt);
            }

            RedisResult result = this.database.ScriptEvaluate(ClaimScript, [Key(sessionId)], arguments.ToArray());
            RedisResult[] expiries = (RedisResult[]?)result ?? [];
            if (expiries.Length != ordered.Length)
            {
                return false;
            }

            var states = new List<CncUploadReceiptState>(ordered.Length);
            for (int index = 0; index < ordered.Length; index++)
            {
                CncUploadReceiptClaim claim = ordered[index];
                long expiry = long.Parse(expiries[index].ToString(), CultureInfo.InvariantCulture);
                states.Add(new CncUploadReceiptState(
                    claim.FormId, claim.SessionId, claim.ItemId, claim.Role, claim.ProtectedReceipt,
                    DateTimeOffset.FromUnixTimeMilliseconds(expiry)));
            }

            claimSet = new CncUploadReceiptClaimSet(states);
            return true;
        }

        public void Restore(CncUploadReceiptClaimSet claimSet, DateTimeOffset now)
        {
            if (claimSet?.Receipts is null || claimSet.Receipts.Count == 0)
            {
                return;
            }

            CncUploadReceiptState[] receipts = claimSet.Receipts.Where(IsValid).ToArray();
            if (receipts.Length == 0)
            {
                return;
            }

            string sessionId = receipts[0].SessionId;
            if (receipts.Any(receipt => !string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("A CNC receipt claim set must belong to one session.");
            }

            var arguments = new List<RedisValue>(2 + (receipts.Length * 6)) { receipts.Length, Milliseconds(now) };
            foreach (CncUploadReceiptState receipt in receipts)
            {
                arguments.Add(Field(receipt.FormId, receipt.ItemId, receipt.Role));
                arguments.Add(Digest(receipt.FormId));
                arguments.Add(Digest(receipt.ItemId));
                arguments.Add(receipt.Role);
                arguments.Add(receipt.ProtectedReceipt);
                arguments.Add(Milliseconds(receipt.ExpiresAtUtc));
            }

            _ = this.database.ScriptEvaluate(RestoreScript, [Key(sessionId)], arguments.ToArray());
        }

        private static RedisKey Key(string sessionId) => KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));

        private static string Field(string formId, string itemId, string role) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formId + "\n" + itemId + "\n" + role)));

        private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private static long Milliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

        private static bool IsValid(CncUploadReceiptState receipt) => receipt is not null
            && !string.IsNullOrWhiteSpace(receipt.FormId) && !string.IsNullOrWhiteSpace(receipt.SessionId)
            && !string.IsNullOrWhiteSpace(receipt.ItemId) && !string.IsNullOrWhiteSpace(receipt.Role)
            && !string.IsNullOrWhiteSpace(receipt.ProtectedReceipt);

        private static bool IsValid(CncUploadReceiptClaim claim) => claim is not null
            && !string.IsNullOrWhiteSpace(claim.FormId) && !string.IsNullOrWhiteSpace(claim.SessionId)
            && !string.IsNullOrWhiteSpace(claim.ItemId) && !string.IsNullOrWhiteSpace(claim.Role)
            && !string.IsNullOrWhiteSpace(claim.ProtectedReceipt);
    }
}
