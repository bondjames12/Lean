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
using QuantConnect.Data.Market;

namespace QuantConnect.Indicators
{
    /// <summary>
    /// The AverageTrueRange indicator is a measure of volatility introduced by Welles Wilder in his
    /// book: New Concepts in Technical Trading Systems. This indicator computes the TrueRange and then
    /// smoothes the TrueRange over a given period.
    ///
    /// TrueRange is defined as the maximum of the following:
    ///   High - Low
    ///   ABS(High - PreviousClose)
    ///   ABS(Low - PreviousClose)
    /// </summary>
    public class AverageTrueRangeStop : BarIndicator, IIndicatorWarmUpPeriodProvider
    {
        private readonly AverageTrueRange _atr;
        private readonly decimal _multiplier;

        private decimal _ts;        // current trailing-stop
        private decimal _prevTs;    // previous trailing-stop
        private decimal _prevClose; // previous close
        private int _trend;     //  1 = long, -1 = short
        private int _prevTrend;
        private bool _isInitialized;

        /// <summary>Current trend direction (1 = long, -1 = short).</summary>
        public int Trend => _trend;

        public override bool IsReady => _atr.IsReady && _isInitialized;

        /// <summary>
        /// Required period, in data points, for the indicator to be ready and fully initialized.
        /// </summary>
        public int WarmUpPeriod { get; }

        public AverageTrueRangeStop(string name, int length = 10, double multiplier = 2.5)
            : base(name)
        {
            if (length <= 0) throw new ArgumentException("length must be > 0", nameof(length));
            if (multiplier <= 0) throw new ArgumentException("multiplier must be > 0", nameof(multiplier));

            _multiplier = (decimal)multiplier;
            _atr = new AverageTrueRange($"{name}_ATR", length, MovingAverageType.Wilders);
            _trend = 1;
            _prevTrend = 1;
        }

        public AverageTrueRangeStop(int length = 10, double multiplier = 2.5)
            : this($"ATRTrailingStop({length},{multiplier})", length, multiplier) { }


        protected override decimal ComputeNextValue(IBaseDataBar input)
        {
            _atr.Update(input);

            // Wait until ATR is ready
            if (!_atr.IsReady)
            {
                _prevClose = input.Close;
                return input.Close;
            }

            var atr = _atr.Current.Value;
            var up = input.Close - (_multiplier * atr);
            var dn = input.Close + (_multiplier * atr);

            if (!_isInitialized)
            {
                _trend = 1;
                _ts = up;
                _isInitialized = true;
            }
            else
            {
                if (_prevClose > _prevTs) up = Math.Max(up, _prevTs);
                if (_prevClose < _prevTs) dn = Math.Min(dn, _prevTs);

                if (_prevTrend == 1 && input.Close < _prevTs) _trend = -1;
                else if (_prevTrend == -1 && input.Close > _prevTs) _trend = 1;
                else _trend = _prevTrend;

                _ts = _trend == 1 ? up : dn;
            }

            _prevClose = input.Close;
            _prevTs = _ts;
            _prevTrend = _trend;

            return _ts;
        }

        public override void Reset()
        {
            base.Reset();
            _atr.Reset();
            _ts = _prevTs = _prevClose = 0m;
            _trend = _prevTrend = 1;
            _isInitialized = false;
        }
    }
}
