/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect.Algorithm;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace QuantConnect.Report
{
    /// <summary>
    /// Fake algorithm that initializes portfolio and algorithm securities. Never ran.
    /// </summary>
    public class PortfolioLooperAlgorithm : QCAlgorithm
    {
        private decimal _startingCash;
        private List<Order> _orders;
        private AlgorithmConfiguration _algorithmConfiguration;

        /// <summary>
        /// Initialize an instance of <see cref="PortfolioLooperAlgorithm"/>
        /// </summary>
        /// <param name="startingCash">Starting algorithm cash</param>
        /// <param name="orders">Orders to use</param>
        /// <param name="algorithmConfiguration">Optional parameter to override default algorithm configuration</param>
        public PortfolioLooperAlgorithm(decimal startingCash, IEnumerable<Order> orders, AlgorithmConfiguration algorithmConfiguration = null) : base()
        {
            _startingCash = startingCash;
            _orders = orders.ToList();
            _algorithmConfiguration = algorithmConfiguration;
        }

        /// <summary>
        /// Initializes all the proper Securities from the orders provided by the user
        /// </summary>
        /// <param name="orders">Orders to use</param>
        public void FromOrders(IEnumerable<Order> orders)
        {
            foreach (var symbol in orders.Select(x => x.Symbol).Distinct())
            {
                Resolution resolution;
                switch (symbol.SecurityType)
                {
                    case SecurityType.Option:
                    case SecurityType.Future:
                        resolution = Resolution.Minute;
                        break;
                    default:
                        resolution = Resolution.Daily;
                        break;
                }

                var configs = SubscriptionManager.SubscriptionDataConfigService.Add(symbol, resolution, false, false);
                var security = Securities.CreateSecurity(symbol, configs, 0m);
                if (symbol.SecurityType == SecurityType.Crypto)
                {
                    security.BuyingPowerModel = new SecurityMarginModel();
                }

                // Set leverage to 10000 to account for unknown leverage values in user algorithms
                security.SetLeverage(10000m);

                // Same idea for fees: AlgorithmConfiguration only carries a BrokerageName, so a
                // custom brokerage model (one that is not in that enum) always comes back as
                // Default here, whose fee model is InteractiveBrokersFeeModel. That model throws
                // for markets IB does not trade, and SecurityHolding.UnrealizedProfit prices a
                // hypothetical close on every portfolio update, so the looper dies on the first
                // update instead of producing a report. Probe the reconstructed model once and
                // fall back to zero fees when it cannot price the security - fees on hypothetical
                // closes are noise in a portfolio reconstruction, and the fees actually charged
                // are already carried by the order events.
                if (!CanPriceOrderFees(security))
                {
                    security.FeeModel = new ConstantFeeModel(0);
                }

                var method = typeof(QCAlgorithm).GetMethod("AddToUserDefinedUniverse", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Invoke(this, new object[] { security, configs });
            }
        }

        /// <summary>
        /// Whether the security's reconstructed fee model can price an order for it
        /// </summary>
        private static bool CanPriceOrderFees(Security security)
        {
            try
            {
                var probe = new MarketOrder(security.Symbol, 1, default(DateTime));
                security.FeeModel.GetOrderFee(new OrderFeeParameters(security, probe));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Initialize this algorithm
        /// </summary>
        public override void Initialize()
        {
            if (_algorithmConfiguration != null)
            {
                SetAccountCurrency(_algorithmConfiguration.AccountCurrency);
                SetBrokerageModel(_algorithmConfiguration.Brokerage, _algorithmConfiguration.AccountType);
                Settings.TradingDaysPerYear = _algorithmConfiguration.TradingDaysPerYear;
            }

            // The setup handlers normally fill this in, and GenerateStatisticsResults
            // dereferences it unconditionally, but this fake algorithm never runs through one.
            // 252 matches the fallback AlgorithmConfiguration itself uses.
            Settings.TradingDaysPerYear ??= 252;

            SetCash(_startingCash);

            if (_orders.Count != 0)
            {
                SetStartDate(_orders.First().Time);
                SetEndDate(_orders.Last().Time);
            }

            SetBenchmark(b => 0);
        }
    }
}
