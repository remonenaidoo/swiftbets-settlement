using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Settlement.Infrastructure.Workers;

public sealed class ReconcilerOptions
{
    public const string SectionName = "Reconciler";

    [Range(1, 3600)]
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>How long a fully evaluated coupon may stay unsettled before it counts as stuck.</summary>
    [Range(1, 3600)]
    public int StuckAfterSeconds { get; set; } = 60;
}
