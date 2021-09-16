using Miningcore.Api.Extensions;
using Miningcore.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Payments
{
    public class FeeCalculator
    {
        private readonly PoolConfig poolConfig;

        public FeeCalculator(PoolConfig poolConfig)
        {
            this.poolConfig = poolConfig ?? throw new ArgumentNullException(nameof(poolConfig));
        }

        public FeeResult Calculate(string address, decimal amount)
        {
            var result = new FeeResult(amount);
            result.CanUsed = address != poolConfig.Address && amount > 0;            
            result.Percentage = poolConfig.IsCustomFeeAddress(address) && poolConfig.CustomFeeAddresses != null ?
                poolConfig.PercentageFeeCustom.Value :
                poolConfig.GetPercentageFeeDefault();

            result.CalculatedAmount = result.CanUsed ?
                amount - (amount * result.Percentage / 100m) :
                0m;

            return result;
        }
    }
}
