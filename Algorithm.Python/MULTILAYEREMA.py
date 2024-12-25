from AlgorithmImports import *

class MULTILAYEREMA(QCAlgorithm):
    def Initialize(self):
        self.SetStartDate(2024, 12, 1)  # Set Start Date
        self.SetEndDate(2024, 12, 20)    # Set End Date
        self.SetCash(100000)           # Set Strategy Cash
        
        # Add the cryptocurrency pairs
        self.symbols = [
            self.AddEquity("TSLA", Resolution.HOUR).Symbol,
            #self.AddCrypto("BTCUSD", Resolution.HOUR).Symbol,
            #self.AddCrypto("ETHUSD", Resolution.HOUR).Symbol,
            #self.AddCrypto("LTCUSD", Resolution.HOUR).Symbol,
            #self.AddCrypto("XRPUSD", Resolution.HOUR).Symbol
        ]
        
        self.indicators = {}
        
        for symbol in self.symbols:
            self.indicators[symbol] = {
                "hammer": self.CandlestickPatterns.Hammer(symbol),
                "hanging_man": self.CandlestickPatterns.HangingMan(symbol),
                "doji": self.CandlestickPatterns.Doji(symbol),
                "spinning_top": self.CandlestickPatterns.SpinningTop(symbol),
                "engulfing": self.CandlestickPatterns.Engulfing(symbol),
                "rsi": self.RSI(symbol, 14, MovingAverageType.Wilders, Resolution.HOUR),
                "sma10": self.SMA(symbol, 10, Resolution.HOUR),
                "sma05": self.SMA(symbol, 5, Resolution.HOUR),
                "ema20": self.EMA(symbol, 20, Resolution.HOUR),
                "sma30": self.SMA(symbol, 30, Resolution.HOUR),
                "sma50": self.SMA(symbol, 50, Resolution.HOUR),
                "sma200": self.SMA(symbol, 200, Resolution.HOUR),
                "sma600": self.SMA(symbol, 600, Resolution.HOUR),
                "sma40": self.SMA(symbol, 40, Resolution.HOUR),
                "sma120": self.SMA(symbol, 120, Resolution.HOUR),
                "ema05": self.EMA(symbol, 5, Resolution.HOUR),
                "ema10": self.EMA(symbol, 10, Resolution.HOUR),
                "ema30": self.EMA(symbol, 30, Resolution.HOUR),
                "ema65": self.EMA(symbol, 65, Resolution.HOUR),
                "ema100": self.EMA(symbol, 100, Resolution.HOUR),
                "ema150": self.EMA(symbol, 150, Resolution.HOUR),
                "ema500": self.EMA(symbol, 500, Resolution.HOUR),
                "ema600": self.EMA(symbol, 600, Resolution.HOUR),
                "entry_price": None,
                "stop_price": None
            }
    
    def OnData(self, data):
        for symbol in self.symbols:
            if not data.ContainsKey(symbol):
                continue
            
            price = data[symbol].Close
            indicators = self.indicators[symbol]
            
            # Buy condition
            if (indicators["rsi"].Current.Value > 40) and (indicators["ema10"].Current.Value > indicators["ema20"].Current.Value > indicators["ema65"].Current.Value > indicators["ema150"].Current.Value):
                if not self.Portfolio[symbol].Invested:
                    self.SetHoldings(symbol, 1)
                    indicators["entry_price"] = price
                    indicators["stop_price"] = price * 0.90 
            
            # Sell condition
            elif indicators["rsi"].Current.Value < 50 and ((indicators["ema10"].Current.Value < indicators["ema65"].Current.Value) or (indicators["ema20"].Current.Value < indicators["ema65"].Current.Value)):
                if self.Portfolio[symbol].Invested:
                    self.Liquidate(symbol)
                    indicators["entry_price"] = None
                    indicators["stop_price"] = None 
            
            # Stop-loss condition
            #if self.Portfolio[symbol].Invested and indicators["entry_price"] is not None:
            #    if price < indicators["entry_price"] * 0.85:
            #        self.Liquidate(symbol)
            #        indicators["entry_price"] = None
            #        indicators["stop_price"] = None
            # 
            # Trailing stop-loss and target
            #if self.Portfolio[symbol].Invested:
            #    indicators["stop_price"] = max(indicators["stop_price"], price * 0.90)
            #    if price < indicators["stop_price"]:
            #        self.Liquidate(symbol)
            #        indicators["stop_price"] = None
            #    if price > indicators["entry_price"] * 1.15:  # Example target
             #       self.Liquidate(symbol)
            #        indicators["entry_price"] = None
            #        indicators["stop_price"] = None
