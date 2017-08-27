// Copyright © SoftAlgoTrade, 2014 - 2017.
//
// Web: https://softalgotrade.com
// Support: https://support.softalgotrade.com
//
// Licensed under the http://www.apache.org/licenses/LICENSE-2.0
//
// Examples are distributed in the hope that they will be useful, but without any warranty.
// It is provided "AS IS" without warranty of any kind, either expressed or implied. 

using SAT.Trading;

namespace Arbitrage
{
    public class FuturesSpreadOnTicks : IIndicator<Trade>
    {
        private decimal _lastValue = Default;

        private readonly decimal _volume;

        public FuturesSpreadOnTicks(decimal volume)
        {
            _volume = volume;
        }

        protected override void OnReset(){ }

        protected override decimal OnAdd(Trade value)
        {
            if (value.Security.InitialMargin > 0)
                _lastValue = value.Price*_volume;

            return _lastValue;
        }
    }

    public class StockSpreadOnTicks : IIndicator<Trade>
    {
        private decimal _lastValue = Default;

        private readonly decimal _volume;

        public StockSpreadOnTicks(decimal volume)
        {
            _volume = volume;
        }

        protected override void OnReset() { }

        protected override decimal OnAdd(Trade value)
        {
            if (value.Security.InitialMargin == 0)
                _lastValue = value.Price * value.Security.LotSize* _volume;

            return _lastValue;
        }
    }

    public class FuturesSpreadOnCandles : IIndicator<Candle>
    {
        private decimal _lastValue = Default;

        private readonly decimal _volume;

        public FuturesSpreadOnCandles(decimal volume)
        {
            _volume = volume;
        }

        protected override void OnReset() { }

        protected override decimal OnAdd(Candle value)
        {
            if (value.Security.InitialMargin > 0)
                _lastValue = value.ClosePrice*_volume;

            return _lastValue;
        }
    }

    public class StockSpreadOnCandles : IIndicator<Candle>
    {
        private decimal _lastValue = Default;

        private readonly decimal _volume;

        public StockSpreadOnCandles(decimal volume)
        {
            _volume = volume;
        }

        protected override void OnReset() { }

        protected override decimal OnAdd(Candle value)
        {
            if (value.Security.InitialMargin == 0)
                _lastValue = value.ClosePrice * value.Security.LotSize* _volume;

            return _lastValue;
        }
    }
}