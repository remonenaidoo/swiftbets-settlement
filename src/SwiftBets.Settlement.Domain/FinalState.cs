namespace SwiftBets.Settlement.Domain;

/// <summary>Why a coupon no longer takes results. Stored on the coupon; null while it is open to settlement.</summary>
public enum FinalState : byte
{
    CashedOut = 1,
}
