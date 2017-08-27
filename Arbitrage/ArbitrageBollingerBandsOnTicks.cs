// Copyright © SoftAlgoTrade, 2014 - 2017.
//
// Web: https://softalgotrade.com
// Support: https://support.softalgotrade.com
//
// Licensed under the http://www.apache.org/licenses/LICENSE-2.0
//
// Examples are distributed in the hope that they will be useful, but without any warranty.
// It is provided "AS IS" without warranty of any kind, either expressed or implied. 

using SAT.Indicators;
using SAT.Trading;

namespace Arbitrage
{
    public class BollingerTopOnTicks : IIndicator<Trade>
    {
        private readonly BollingerBandsTop _bollingerBandsTop;

        private decimal _stock;
        private decimal _futures;

        public BollingerTopOnTicks(int period, int standardDeviation)
        {
            _bollingerBandsTop = new BollingerBandsTop(period, standardDeviation);
        }

        protected override void OnReset()
        {
            _bollingerBandsTop.Reset();
        }

        protected override decimal OnAdd(Trade value)
        {
            if (value.Security.InitialMargin > 0)
                _futures = value.Price;
            else
                _stock = value.Price * value.Security.LotSize;

            if (_stock != 0 && _futures != 0)
                return _bollingerBandsTop.Add(_futures - _stock);

            return Default;
        }
    }

    public class BollingerAverageOnTicks : IIndicator<Trade>
    {
        private readonly BollingerBandsAverage _bollingerBandsAverage;

        private decimal _stock;
        private decimal _futures;

        public BollingerAverageOnTicks(int period)
        {
            _bollingerBandsAverage = new BollingerBandsAverage(period);
        }

        protected override void OnReset()
        {
            _bollingerBandsAverage.Reset();
        }

        protected override decimal OnAdd(Trade value)
        {
            if (value.Security.InitialMargin > 0)
                _futures = value.Price;
            else
                _stock = value.Price*value.Security.LotSize;

            if (_stock != 0 && _futures != 0)
                return _bollingerBandsAverage.Add(_futures - _stock);

            return Default;
        }
    }

    public class BollingerBottomOnTicks : IIndicator<Trade>
    {
        private readonly BollingerBandsBottom _bollingerBandsBottom;

        private decimal _stock;
        private decimal _futures;

        public BollingerBottomOnTicks(int period, int standardDeviation)
        {
            _bollingerBandsBottom = new BollingerBandsBottom(period, standardDeviation);
        }

        protected override void OnReset()
        {
            _bollingerBandsBottom.Reset();
        }

        protected override decimal OnAdd(Trade value)
        {
            if (value.Security.InitialMargin > 0)
                _futures = value.Price;
            else
                _stock = value.Price * value.Security.LotSize;

            if (_stock != 0 && _futures != 0)
                return _bollingerBandsBottom.Add(_futures - _stock);

            return Default;
        }
    }
}