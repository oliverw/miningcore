using JetBrains.Annotations;

namespace Miningcore.Persistence.Model.Projections;

[UsedImplicitly]
public class AmountByDate
{
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}
