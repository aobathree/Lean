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

using System;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// End-to-end live test for the bitbank brokerage (environment: live-bitbank).
    /// Verifies the full engine path: live ticks reach OnData, an order placed through
    /// the algorithm API flows REST -> private stream -> OnOrderEvent, and cancellation
    /// completes. The order is a minimum-lot (0.0001 BTC) post-only limit buy at 50%
    /// of the market price, so it cannot fill; it is canceled 90 seconds after placement
    /// and the algorithm quits itself once the cancel is confirmed.
    /// </summary>
    public class BitbankE2ETestAlgorithm : QCAlgorithm
    {
        private Symbol _symbol;
        private OrderTicket _ticket;
        private DateTime _placedAtUtc;
        private bool _cancelRequested;
        private long _dataCount;

        /// <summary>
        /// Configures the brokerage model, the BTCJPY subscription and post-only default order properties
        /// </summary>
        public override void Initialize()
        {
            // backtest fallback values; in live mode cash and time come from the brokerage
            SetStartDate(2026, 1, 1);
            SetCash("JPY", 100000);

            SetBrokerageModel(BrokerageName.Bitbank, AccountType.Cash);
            _symbol = AddCrypto("BTCJPY", Resolution.Second, Market.Bitbank).Symbol;
            SetBenchmark(_symbol);

            // post-only so the test order can never take liquidity
            DefaultOrderProperties = new BitbankOrderProperties
            {
                PostOnly = true,
                TimeInForce = TimeInForce.GoodTilCanceled
            };

            Log("BitbankE2ETestAlgorithm: initialized, waiting for live BTCJPY data...");
        }

        /// <summary>
        /// Places the test order on first priced data, cancels it after 90 seconds, quits when canceled
        /// </summary>
        public override void OnData(Slice slice)
        {
            _dataCount++;
            var price = Securities[_symbol].Price;

            // heartbeat: confirm live data is flowing without spamming the log
            if (_dataCount == 1 || _dataCount % 60 == 0)
            {
                Log($"OnData #{_dataCount}: BTCJPY price={price} bid={Securities[_symbol].BidPrice} ask={Securities[_symbol].AskPrice}");
            }

            if (_ticket == null)
            {
                if (price <= 0)
                {
                    return;
                }
                // min lot 0.0001 BTC at 50% of market, integer price (btc_jpy tick size = 1 JPY)
                var limitPrice = Math.Floor(price * 0.5m);
                _ticket = LimitOrder(_symbol, 0.0001m, limitPrice, tag: "bitbank E2E test order");
                _placedAtUtc = UtcTime;
                Log($"E2E: placed limit buy 0.0001 BTCJPY @ {limitPrice} (order id {_ticket.OrderId})");
                return;
            }

            if (!_cancelRequested && UtcTime >= _placedAtUtc.AddSeconds(90))
            {
                Log("E2E: 90s elapsed, canceling the test order");
                _ticket.Cancel();
                _cancelRequested = true;
                return;
            }

            if (_cancelRequested && _ticket.Status == OrderStatus.Canceled)
            {
                Log($"E2E: SUCCESS - data ticks={_dataCount}, order lifecycle Submitted->Canceled confirmed via engine");
                Quit("bitbank E2E test completed successfully");
            }
            else if (_cancelRequested && UtcTime >= _placedAtUtc.AddSeconds(300))
            {
                Error($"E2E: TIMEOUT - cancel not confirmed within 300s, last order status: {_ticket.Status}");
                Quit("bitbank E2E test timed out waiting for cancellation");
            }
        }

        /// <summary>
        /// Logs every order event so the REST/stream flow is visible in the live log
        /// </summary>
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Log($"E2E OnOrderEvent: {orderEvent}");
        }
    }
}
