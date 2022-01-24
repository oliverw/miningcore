using Miningcore.Persistence.Model;

namespace Miningcore.Api.Responses
{
    public class SubtractBalanceResponse
    {
        public Balance OldBalance { get; set; }
        public Balance NewBalance { get; set; }
    }
}
