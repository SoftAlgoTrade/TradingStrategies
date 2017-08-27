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
    public class FuturesPriceOnTicks : IIndicator<Trade>
    {
        protected override void OnReset() { }

        protected override decimal OnAdd(Trade value) => value.Security.InitialMargin > 0 ? value.Price : Default;
    }

    public class StockPriceOnTicks : IIndicator<Trade>
    {
        protected override void OnReset() { }

        protected override decimal OnAdd(Trade value) => value.Security.InitialMargin == 0 ? value.Price : Default;
    }

    public class FuturesPriceOnCandles : IIndicator<Candle>
    {
        protected override void OnReset() { }

        protected override decimal OnAdd(Candle value) => value.Security.InitialMargin > 0 ? value.ClosePrice : Default;
    }

    public class StockPriceOnCandles : IIndicator<Candle>
    {
        protected override void OnReset() { }

        protected override decimal OnAdd(Candle value) => value.Security.InitialMargin == 0 ? value.ClosePrice : Default;
    }
}