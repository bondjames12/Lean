using System;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace QuantConnect.Indicators
{
    /// <summary>
    /// BXTrender indicator based on B-Xtrender 
    /// Ported by Colin Weber from Tradingview: B-Xtrender @Puppytherapy v3
    /// All credit to original author: https://www.tradingview.com/script/YHZimEz8-B-Xtrender-Puppytherapy/
    /// and https://ifta.org/public/files/journal/d_ifta_journal_19.pdf IFTA Journal by Bharat Jhunjhunwala
    /// </summary>
    public class BXTrender : WindowIndicator<IndicatorDataPoint>, IIndicatorWarmUpPeriodProvider
    {
        private int _shortL1, _shortL2, _shortL3;

        //public decimal CurrentHistogram => IsReady ? this.Current.Value : 0m;

        // Color state properties for external use
        //public bool IsIncreasing => IsReady && this[0] > this[1];
        //public bool IsPositive => IsReady && this[0] > 0;

        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => _finalOutput.IsReady && Samples > WarmUpPeriod;

        /// <summary>
        /// Required period, in data points, for the indicator to be ready and fully initialized.
        /// </summary>
        public int WarmUpPeriod { get; }

        private readonly ExponentialMovingAverage _ema1;
        private readonly ExponentialMovingAverage _ema2;
        private readonly CompositeIndicator _emaDiff;
        private readonly RelativeStrengthIndex _rsi;
        private readonly CompositeIndicator _finalOutput;

        /// <summary>
        /// Initializes a new instance of the BXTrender class
        /// </summary>
        public BXTrender(string name, int shortL1, int shortL2, int shortL3)
            : base(name, Math.Max(shortL1,shortL2))
        {
            _shortL1 = shortL1;
            _shortL2 = shortL2;
            _shortL3 = shortL3;

            if (Math.Min(_shortL1, Math.Max(_shortL2, _shortL3)) < 2) throw new ArgumentException("Periods must be at least 2");

            // Initialize EMAs,
            _ema1 = new ExponentialMovingAverage(_shortL1);
            _ema2 = new ExponentialMovingAverage(_shortL2);

            // Create EMA difference
            _emaDiff = _ema1.Minus(_ema2);

            // Create RSI of EMA difference
            _rsi = new RelativeStrengthIndex(_shortL3);
            var rsiOfEmaDiff = IndicatorExtensions.Of(_rsi, _emaDiff);

            // Create final output (RSI - 50)
            _finalOutput = rsiOfEmaDiff.Minus(50m);

            // Calculate warm-up period (Max EMA periods + RSI warm-up)
            var maxEmaPeriod = Math.Max(_shortL1, _shortL2);
            WarmUpPeriod = maxEmaPeriod + _rsi.WarmUpPeriod;

            // Register dependencies for automatic updates
            //RegisterIndicator(_ema1, Update);
            //RegisterIndicator(_ema2, Update);
            //RegisterIndicator(_emaDiff, Update);
            //RegisterIndicator(_rsi, Update);
            //RegisterIndicator(_finalOutput, Update);
        }

        /// <summary>
        /// Computes the next value for this indicator from the given state.
        /// </summary>
        /// <param name="window">The window of data held in this indicator</param>
        /// <param name="input">The input value to this indicator on this time step</param>
        /// <returns>A new value for this indicator</returns>
        protected override decimal ComputeNextValue(IReadOnlyWindow<IndicatorDataPoint> window, IndicatorDataPoint input)
        {
            //if (!IsReady)
            //    return 0;
            // Update all registered indicators with the new input
            _ema1.Update(input);
            _ema2.Update(input);
            return _finalOutput.Current.Value;
        }

        /// <summary>
        /// Resets this indicator to its initial state
        /// </summary>
        public override void Reset()
        {
            _ema1.Reset();
            _ema2.Reset();
            _emaDiff.Reset();
            _rsi.Reset();
            _finalOutput.Reset();
            base.Reset();
        }

        
    }
}
