namespace SwiftBets.Settlement.Domain;

public enum ResultState : byte
{
    Provisional = 1,
    Official = 2,
    Correction = 3,
    Void = 4,
}
