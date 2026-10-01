using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Immutable local display rows; never a protected quote or readiness signal.</summary>
public sealed class InstantQuotationMaterialPriceDisplaySnapshot
{
    public InstantQuotationMaterialPriceDisplaySnapshot(long generation)
        : this(generation, []) { }

    private InstantQuotationMaterialPriceDisplaySnapshot(long generation,
        InstantQuotationMaterialPricingProgress[] entries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        Generation = generation;
        Entries = Array.AsReadOnly(entries);
    }

    public long Generation { get; }

    public IReadOnlyList<InstantQuotationMaterialPricingProgress> Entries { get; }

    /// <summary>Creates a copied display snapshot after a legal per-part/material transition.</summary>
    public InstantQuotationMaterialPriceDisplaySnapshot Apply(InstantQuotationMaterialPricingProgress frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PartId == Guid.Empty || string.IsNullOrWhiteSpace(frame.MaterialKey)
            || !Enum.IsDefined(frame.Status)
            || (frame.Status == InstantQuotationMaterialPricingStatus.Completed
                ? frame.UnitPrice is not { } amount || !double.IsFinite(amount) || amount < 0
                : frame.UnitPrice is not null))
            throw new ArgumentException("The material display frame is invalid.", nameof(frame));

        var entries = Entries.ToArray();
        var index = Array.FindIndex(entries, entry => entry.PartId == frame.PartId
            && string.Equals(entry.MaterialKey, frame.MaterialKey, StringComparison.Ordinal));
        if (index < 0)
        {
            if (frame.Status != InstantQuotationMaterialPricingStatus.Pending)
                throw new InvalidOperationException("A material display row must begin pending.");
            return new(Generation, [.. entries, frame]);
        }
        if (entries[index] == frame) return this;
        if (entries[index].Status != InstantQuotationMaterialPricingStatus.Pending
            || frame.Status == InstantQuotationMaterialPricingStatus.Pending)
            throw new InvalidOperationException("The material display row is already terminal.");
        entries[index] = frame;
        return new(Generation, entries);
    }
}
