namespace Miningcore.Persistence.Model.Projections;

public record AmountByDate
{
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
}
