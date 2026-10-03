namespace SwiftBets.Settlement.Application.Ports;

/// <summary>Position is the leg's place on the coupon; bet lines index the non-banker legs in that order.</summary>
/// <summary>Builder is a bet builder's <see cref="Contracts.Offer.BetBuilderLegV1"/> as JSON, null for any other leg.</summary>
public sealed record IndexedLeg(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds, bool IsBanker = false, int Position = 0, string? Builder = null)
{
    public Contracts.Offer.BetBuilderLegV1? BuilderLeg => Builder is null ? null
        : System.Text.Json.JsonSerializer.Deserialize<Contracts.Offer.BetBuilderLegV1>(Builder, Contracts.Serialization.ContractJson.Options);

    public static string? Serialize(Contracts.Offer.BetBuilderLegV1? builder) =>
        builder is null ? null : System.Text.Json.JsonSerializer.Serialize(builder, Contracts.Serialization.ContractJson.Options);
}
