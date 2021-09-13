using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Payments
{
    public class FeeResult
    {
        public FeeResult(decimal originalAmount)
        {
            OriginalAmount = originalAmount;
        }

        public decimal Percentage { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal OriginalAmount { get; }
        public bool CanUsed { get; set; }
    }
}
