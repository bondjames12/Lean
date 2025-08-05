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
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data.Market;

namespace QuantConnect.Indicators
{
    /// <summary>
    /// Represents the type of a pivot point for the Smart Money Zones indicator.
    /// </summary>
    public enum SmartMoneyPivotPointType
    {
        High,
        Low
    }

    /// <summary>
    /// Represents a pivot point for the Smart Money Zones indicator.
    /// </summary>
    public class SmartMoneyPivotPoint
    {
        public SmartMoneyPivotPointType Type { get; }
        public int BarIndex { get; }
        public int FoundBarIndex { get; }
        public decimal Level { get; }
        public decimal HighLow { get; set; }
        public decimal Fib0_618 { get; set; }
        public decimal Fib0_786 { get; set; }
        public decimal Fib0_826 { get; set; }

        public SmartMoneyPivotPoint(SmartMoneyPivotPointType type, int barIndex, int foundBarIndex, decimal level, decimal highLow)
        {
            Type = type;
            BarIndex = barIndex;
            FoundBarIndex = foundBarIndex;
            Level = level;
            HighLow = highLow;
        }
    }

    /// <summary>
    /// Represents the Fibonacci boxes.
    /// </summary>
    public class FibBox
    {
        // In a real implementation, you would use charting objects to draw boxes and lines.
        // For simplicity, we'll just store the values.
        public decimal InstitutionalTop { get; set; }
        public decimal InstitutionalBottom { get; set; }
        public decimal SmartMoneyTop { get; set; }
        public decimal SmartMoneyBottom { get; set; }
    }

    /// <summary>
    /// Represents the Smart Money Zones indicator.
    /// </summary>
    public class SmartMoneyZones : IndicatorBase<IBaseDataBar>, IIndicatorWarmUpPeriodProvider
    {
        private readonly int _pivotLeft;
        private readonly int _pivotRight;

        private readonly RollingWindow<IBaseDataBar> _window;
        private readonly List<SmartMoneyPivotPoint> _phArray = new List<SmartMoneyPivotPoint>();
        private readonly List<SmartMoneyPivotPoint> _plArray = new List<SmartMoneyPivotPoint>();

        private readonly List<FibBox> _buyFibBoxes = new List<FibBox>();
        private readonly List<FibBox> _sellFibBoxes = new List<FibBox>();

        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => Samples > WarmUpPeriod;

        /// <summary>
        /// Required period, in data points, for the indicator to be ready and fully initialized.
        /// </summary>
        public int WarmUpPeriod { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartMoneyZones"/> class.
        /// </summary>
        /// <param name="name">The name of this indicator.</param>
        /// <param name="pivotLeft">The number of bars to the left of the pivot.</param>
        /// <param name="pivotRight">The number of bars to the right of the pivot.</param>
        public SmartMoneyZones(string name, int pivotLeft, int pivotRight)
            : base(name)
        {
            _pivotLeft = pivotLeft;
            _pivotRight = pivotRight;
            var windowSize = pivotLeft + pivotRight + 1;
            _window = new RollingWindow<IBaseDataBar>(windowSize);
            WarmUpPeriod = windowSize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartMoneyZones"/> class.
        /// </summary>
        /// <param name="pivotLeft">The number of bars to the left of the pivot.</param>
        /// <param name="pivotRight">The number of bars to the right of the pivot.</param>
        public SmartMoneyZones(int pivotLeft = 5, int pivotRight = 5)
            : this($"SMZ({pivotLeft},{pivotRight})", pivotLeft, pivotRight)
        {
        }

        /// <summary>
        /// Computes the next value of this indicator from the given state
        /// </summary>
        /// <param name="input">The input given to the indicator</param>
        /// <returns>A new value for this indicator</returns>
        protected override decimal ComputeNextValue(IBaseDataBar input)
        {
            _window.Add(input);

            if (!_window.IsReady)
            {
                return 0m;
            }

            var middleBar = _window[_pivotRight];

            // Check for pivot high
            var isPivotHigh = true;
            for (var i = 0; i < _window.Count; i++)
            {
                if (i == _pivotRight) continue;
                if (_window[i].High >= middleBar.High)
                {
                    isPivotHigh = false;
                    break;
                }
            }

            // Check for pivot low
            var isPivotLow = true;
            for (var i = 0; i < _window.Count; i++)
            {
                if (i == _pivotRight) continue;
                if (_window[i].Low <= middleBar.Low)
                {
                    isPivotLow = false;
                    break;
                }
            }

            var currentBarIndex = (int)Samples - 1;
            var pivotBarIndex = currentBarIndex - _pivotRight;

            if (isPivotHigh)
            {
                var lowestSincePh = _window.Select(x => x.Low).Min();
                var pp = new SmartMoneyPivotPoint(SmartMoneyPivotPointType.High, pivotBarIndex, currentBarIndex, middleBar.High, lowestSincePh);
                UpdateFibLevels(pp);
                _phArray.Insert(0, pp);
            }

            if (isPivotLow)
            {
                var highestSincePl = _window.Select(x => x.High).Max();
                var pp = new SmartMoneyPivotPoint(SmartMoneyPivotPointType.Low, pivotBarIndex, currentBarIndex, middleBar.Low, highestSincePl);
                UpdateFibLevels(pp);
                _plArray.Insert(0, pp);
            }

            RemoveMitigated(_plArray, input.Low, true);
            RemoveMitigated(_phArray, input.High, false);

            foreach (var ph in _phArray)
            {
                if (input.Low < ph.HighLow)
                {
                    ph.HighLow = input.Low;
                    UpdateFibLevels(ph);
                }
            }

            foreach (var pl in _plArray)
            {
                if (input.High > pl.HighLow)
                {
                    pl.HighLow = input.High;
                    UpdateFibLevels(pl);
                }
            }

            // Further logic to be implemented
            var insideBuyZone = 0;
            var insideSellZone = 0;

            _buyFibBoxes.Clear();
            for (var i = 0; i < _plArray.Count; i++)
            {
                var pl = _plArray[i];
                var box = DrawBuyZone(pl, i);
                _buyFibBoxes.Add(box);
                if (input.Close < pl.Fib0_618 && input.Close > pl.Fib0_826)
                {
                    insideBuyZone = 1;
                }
            }

            _sellFibBoxes.Clear();
            for (var i = 0; i < _phArray.Count; i++)
            {
                var ph = _phArray[i];
                var box = DrawSellZone(ph, i);
                _sellFibBoxes.Add(box);
                if (input.Close > ph.Fib0_618 && input.Close < ph.Fib0_826)
                {
                    insideSellZone = 1;
                }
            }

            if (insideBuyZone == 1 && insideSellZone == 1) return 3;
            if (insideBuyZone == 1) return 1;
            if (insideSellZone == 1) return 2;

            return 0m;
        }

        private FibBox DrawBuyZone(SmartMoneyPivotPoint pl, int i)
        {
            return new FibBox
            {
                InstitutionalTop = pl.Fib0_618,
                InstitutionalBottom = pl.Fib0_786,
                SmartMoneyTop = pl.Fib0_786,
                SmartMoneyBottom = pl.Fib0_826
            };
        }

        private FibBox DrawSellZone(SmartMoneyPivotPoint ph, int i)
        {
            return new FibBox
            {
                InstitutionalTop = ph.Fib0_618,
                InstitutionalBottom = ph.Fib0_786,
                SmartMoneyTop = ph.Fib0_786,
                SmartMoneyBottom = ph.Fib0_826
            };
        }

        private void RemoveMitigated(List<SmartMoneyPivotPoint> pArray, decimal target, bool isPl)
        {
            pArray.RemoveAll(p => isPl ? target < p.Level : target > p.Level);
        }

        private void UpdateFibLevels(SmartMoneyPivotPoint pp)
        {
            if (pp.Level < pp.HighLow)
            {
                var fibRange = pp.HighLow - pp.Level;
                pp.Fib0_618 = pp.HighLow - (fibRange * 0.618m);
                pp.Fib0_786 = pp.HighLow - (fibRange * 0.786m);
                pp.Fib0_826 = pp.HighLow - (fibRange * 0.826m);
            }
            else
            {
                var fibRange = pp.Level - pp.HighLow;
                pp.Fib0_618 = pp.HighLow + (fibRange * 0.618m);
                pp.Fib0_786 = pp.HighLow + (fibRange * 0.786m);
                pp.Fib0_826 = pp.HighLow + (fibRange * 0.826m);
            }
        }

        /// <summary>
        /// Resets this indicator to its initial state
        /// </summary>
        public override void Reset()
        {
            _phArray.Clear();
            _plArray.Clear();
            _buyFibBoxes.Clear();
            _sellFibBoxes.Clear();
            _window.Reset();
            base.Reset();
        }
    }
}
