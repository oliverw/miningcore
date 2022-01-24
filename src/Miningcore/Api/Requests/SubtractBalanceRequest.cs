namespace Miningcore.Api.Requests
{
    public class SubtractBalanceRequest
    {
        public string PoolId { get; set; }
        public string Address { get; set; }
        public decimal Amount { get; set; }
    }
}
