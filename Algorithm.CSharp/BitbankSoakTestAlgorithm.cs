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
using QuantConnect.Data.Market;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Long-running stability (soak) test for the bitbank brokerage (P5). Observation only:
    /// NEVER places orders. Subscribes BTCJPY and XRPJPY at second resolution, logs an hourly
    /// health line (tick counters, current quotes, disconnect/reconnect counts) and quits
    /// after the configured duration (BITBANK_SOAK_HOURS environment variable, default 25h,
    /// long enough to cross two PubNub 12h token TTLs).
    /// </summary>
    public class BitbankSoakTestAlgorithm : QCAlgorithm
    {
        private Symbol _btcJpy;
        private Symbol _xrpJpy;
        private DateTime _startedUtc;
        private DateTime _lastHealthLogUtc;
        private TimeSpan _duration;

        private long _dataEvents;
        private long _lastHourDataEvents;
        private int _disconnects;
        private int _reconnects;

        /// <summary>
        /// Configures the subscriptions; no trading is performed by this algorithm
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2026, 1, 1);
            SetCash("JPY", 100000);

            SetBrokerageModel(BrokerageName.Bitbank, AccountType.Cash);
            _btcJpy = AddCrypto("BTCJPY", Resolution.Second, Market.Bitbank).Symbol;
            _xrpJpy = AddCrypto("XRPJPY", Resolution.Second, Market.Bitbank).Symbol;
            SetBenchmark(_btcJpy);

            var hours = 25.0;
            var configured = Environment.GetEnvironmentVariable("BITBANK_SOAK_HOURS");
            if (!string.IsNullOrEmpty(configured) && double.TryParse(configured, out var parsed) && parsed > 0)
            {
                hours = parsed;
            }
            _duration = TimeSpan.FromHours(hours);
            _startedUtc = UtcTime;
            _lastHealthLogUtc = UtcTime;

            Log($"SOAK: started, duration={_duration.TotalHours}h, symbols=BTCJPY,XRPJPY");
        }

        /// <summary>
        /// Counts data events, emits the hourly health line and stops after the configured duration
        /// </summary>
        public override void OnData(Slice slice)
        {
            _dataEvents++;

            if (_dataEvents == 1)
            {
                Log($"SOAK: first data received {(UtcTime - _startedUtc).TotalSeconds:F1}s after start");
            }

            if (UtcTime - _lastHealthLogUtc >= TimeSpan.FromHours(1))
            {
                _lastHealthLogUtc = UtcTime;
                var elapsed = UtcTime - _startedUtc;
                var btc = Securities[_btcJpy];
                var xrp = Securities[_xrpJpy];
                Log($"SOAK HEALTH: elapsed={elapsed.TotalHours:F1}h events={_dataEvents} (+{_dataEvents - _lastHourDataEvents}/h) " +
                    $"BTCJPY bid={btc.BidPrice} ask={btc.AskPrice} XRPJPY bid={xrp.BidPrice} ask={xrp.AskPrice} " +
                    $"disconnects={_disconnects} reconnects={_reconnects}");
                _lastHourDataEvents = _dataEvents;
            }

            if (UtcTime - _startedUtc >= _duration)
            {
                Log($"SOAK: COMPLETED - {_duration.TotalHours}h elapsed, events={_dataEvents} " +
                    $"disconnects={_disconnects} reconnects={_reconnects}");
                Quit("bitbank soak test completed");
            }
        }

        /// <summary>
        /// Records brokerage disconnects reported by the engine
        /// </summary>
        public override void OnBrokerageDisconnect()
        {
            _disconnects++;
            Log($"SOAK: brokerage DISCONNECT #{_disconnects} at {UtcTime:O}");
        }

        /// <summary>
        /// Records brokerage reconnects reported by the engine
        /// </summary>
        public override void OnBrokerageReconnect()
        {
            _reconnects++;
            Log($"SOAK: brokerage RECONNECT #{_reconnects} at {UtcTime:O}");
        }
    }
}
