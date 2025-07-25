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
using System.IO;
using System.Runtime.CompilerServices;
using QuantConnect.Data.Market;
using QuantConnect.Python;
using QuantConnect.Util;

namespace QuantConnect.Data.UniverseSelection
{
    /// <summary>
    /// Represents a universe of options data
    /// </summary>
    public class OptionUniverse : BaseChainUniverseData
    {
        /// <summary>
        /// Cache for the symbols to avoid creating them multiple times
        /// </summary>
        /// <remarks>Key: securityType, market, ticker, expiry, strike, right</remarks>
        private static readonly Dictionary<(SecurityType, string, string, DateTime, decimal, OptionRight), Symbol> _symbolsCache = new();

        private const int StartingGreeksCsvIndex = 7;

        /// <summary>
        /// Open interest value of the option
        /// </summary>
        public override decimal OpenInterest
        {
            get
            {
                ThrowIfNotAnOption(nameof(OpenInterest));
                return base.OpenInterest;
            }
        }

        /// <summary>
        /// Implied volatility value of the option
        /// </summary>
        public decimal ImpliedVolatility
        {
            get
            {
                ThrowIfNotAnOption(nameof(ImpliedVolatility));
                return CsvLine.GetDecimalFromCsv(6);
            }
        }

        /// <summary>
        /// Greeks values of the option
        /// </summary>
        public Greeks Greeks
        {
            get
            {
                ThrowIfNotAnOption(nameof(Greeks));
                return new PreCalculatedGreeks(CsvLine);
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class
        /// </summary>
        public OptionUniverse()
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class
        /// </summary>
        public OptionUniverse(DateTime date, Symbol symbol, string csv)
            : base(date, symbol, csv)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class as a copy of the given instance
        /// </summary>
        public OptionUniverse(OptionUniverse other)
            : base(other)
        {
        }

        /// <summary>
        /// Reader converts each line of the data source into BaseData objects. Each data type creates its own factory method, and returns a new instance of the object
        /// each time it is called.
        /// </summary>
        /// <param name="config">Subscription data config setup object</param>
        /// <param name="stream">Stream reader of the source document</param>
        /// <param name="date">Date of the requested data</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>Instance of the T:BaseData object generated by this line of the CSV</returns>
        [StubsIgnore]
        public override BaseData Reader(SubscriptionDataConfig config, StreamReader stream, DateTime date, bool isLiveMode)
        {
            if (stream == null || stream.EndOfStream)
            {
                return null;
            }

            var firstChar = (char)stream.Peek();
            if (firstChar == '#')
            {
                // Skip header
                stream.ReadLine();
                return null;
            }

            Symbol symbol;
            if (!char.IsDigit(firstChar))
            {
                // This is the underlying line
                symbol = config.Symbol.Underlying;
                // Skip the first 3 cells, expiry, strike and right, which will be empty for the underlying
                stream.GetChar();
                stream.GetChar();
                stream.GetChar();
            }
            else
            {
                var expiry = stream.GetDateTime("yyyyMMdd");
                var strike = stream.GetDecimal();
                var right = char.ToUpperInvariant(stream.GetChar()) == 'C' ? OptionRight.Call : OptionRight.Put;
                var targetOption = config.Symbol.SecurityType != SecurityType.IndexOption ? null : config.Symbol.ID.Symbol;

                var cacheKey = (config.SecurityType, config.Market, targetOption ?? config.Symbol.Underlying.Value, expiry, strike, right);
                if (!TryGetCachedSymbol(cacheKey, out symbol))
                {
                    symbol = Symbol.CreateOption(config.Symbol.Underlying, targetOption, config.Symbol.ID.Market,
                        config.Symbol.SecurityType.DefaultOptionStyle(), right, strike, expiry);
                    CacheSymbol(cacheKey, symbol);
                }
            }

            return new OptionUniverse(date, symbol, stream.ReadLine());
        }

        /// <summary>
        /// Adds a new data point to this collection.
        /// If the data point is for the underlying, it will be stored in the <see cref="BaseDataCollection.Underlying"/> property.
        /// </summary>
        /// <param name="newDataPoint">The new data point to add</param>
        public override void Add(BaseData newDataPoint)
        {
            if (newDataPoint is BaseChainUniverseData optionUniverseDataPoint)
            {
                if (optionUniverseDataPoint.Symbol.HasUnderlying)
                {
                    optionUniverseDataPoint.Underlying = Underlying;
                    base.Add(optionUniverseDataPoint);
                }
                else
                {
                    Underlying = optionUniverseDataPoint;
                    foreach (BaseChainUniverseData data in Data)
                    {
                        data.Underlying = optionUniverseDataPoint;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a copy of the instance
        /// </summary>
        /// <returns>Clone of the instance</returns>
        public override BaseData Clone()
        {
            return new OptionUniverse(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetOptionSymbolCsv(Symbol symbol)
        {
            if (!symbol.SecurityType.IsOption())
            {
                return ",,";
            }

            return $"{symbol.ID.Date:yyyyMMdd},{symbol.ID.StrikePrice},{(symbol.ID.OptionRight == OptionRight.Call ? 'C' : 'P')}";
        }

        /// <summary>
        /// Gets the CSV string representation of this universe entry
        /// </summary>
        public static string ToCsv(Symbol symbol, decimal open, decimal high, decimal low, decimal close, decimal volume, decimal? openInterest,
            decimal? impliedVolatility, Greeks greeks)
        {
            if (symbol.SecurityType == SecurityType.Future || symbol.SecurityType == SecurityType.FutureOption)
            {
                return $"{GetOptionSymbolCsv(symbol)},{open},{high},{low},{close},{volume},{openInterest}";
            }

            return $"{GetOptionSymbolCsv(symbol)},{open},{high},{low},{close},{volume},"
                + $"{openInterest},{impliedVolatility},{greeks?.Delta},{greeks?.Gamma},{greeks?.Vega},{greeks?.Theta},{greeks?.Rho}";
        }

        /// <summary>
        /// Implicit conversion into <see cref="Symbol"/>
        /// </summary>
        /// <param name="data">The option universe data to be converted</param>
#pragma warning disable CA2225 // Operator overloads have named alternates
        public static implicit operator Symbol(OptionUniverse data)
#pragma warning restore CA2225 // Operator overloads have named alternates
        {
            return data.Symbol;
        }

        /// <summary>
        /// Gets the CSV header string for this universe entry
        /// </summary>
        public static string CsvHeader(SecurityType securityType)
        {
            // FOPs don't have greeks
            if (securityType == SecurityType.FutureOption || securityType == SecurityType.Future)
            {
                return "expiry,strike,right,open,high,low,close,volume,open_interest";
            }

            return "expiry,strike,right,open,high,low,close,volume,open_interest,implied_volatility,delta,gamma,vega,theta,rho";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfNotAnOption(string propertyName)
        {
            if (!Symbol.SecurityType.IsOption())
            {
                throw new InvalidOperationException($"{propertyName} is only available for options.");
            }
        }

        /// <summary>
        /// Pre-calculated greeks lazily parsed from csv line.
        /// It parses the greeks values from the csv line only when they are requested to avoid holding decimals in memory.
        /// </summary>
        private class PreCalculatedGreeks : Greeks
        {
            private readonly string _csvLine;

            public override decimal Delta => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex);

            public override decimal Gamma => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 1);

            public override decimal Vega => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 2);

            public override decimal Theta => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 3);

            public override decimal Rho => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 4);

            [PandasIgnore]
            public override decimal Lambda => decimal.Zero;

            /// <summary>
            /// Initializes a new default instance of the <see cref="PreCalculatedGreeks"/> class
            /// </summary>
            public PreCalculatedGreeks(string csvLine)
            {
                _csvLine = csvLine;
            }

            /// <summary>
            /// Gets a string representation of the greeks values
            /// </summary>
            public override string ToString()
            {
                return $"D: {Delta}, G: {Gamma}, V: {Vega}, T: {Theta}, R: {Rho}";
            }
        }

        /// <summary>
        /// Tries to get a symbol from the cache
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static bool TryGetCachedSymbol((SecurityType, string, string, DateTime, decimal, OptionRight) key, out Symbol symbol)
        {
            lock (_symbolsCache)
            {
                return _symbolsCache.TryGetValue(key, out symbol);
            }
        }

        /// <summary>
        /// Caches a symbol
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static void CacheSymbol((SecurityType, string, string, DateTime, decimal, OptionRight) key, Symbol symbol)
        {
            lock (_symbolsCache)
            {
                // limit the cache size to help with memory usage
                if (_symbolsCache.Count >= 500000)
                {
                    _symbolsCache.Clear();
                }
                _symbolsCache.TryAdd(key, symbol);
            }
        }
    }
}
