// Copyright © SoftAlgoTrade, 2014 - 2017.
//
// Web: https://softalgotrade.com
// Support: https://support.softalgotrade.com
//
// Licensed under the http://www.apache.org/licenses/LICENSE-2.0
//
// Examples are distributed in the hope that they will be useful, but without any warranty.
// It is provided "AS IS" without warranty of any kind, either expressed or implied.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using SAT.Indicators;
using SAT.Trading;

namespace TurtleTraders
{
    // Описание:
    // За основу взята стратегия Черепах - http://www.benvanvliet.net/Downloads/turtlerules.pdf
    // Описание модификации - https://support.softalgotrade.com/forums/topic/turtle-system-revisited

    // Исходные данные: свечки или тики. Рассмотрены оба варианта.
    // Свечки используются для расчета индикаторов.
    // Для более точного входа в позицию и расчета стоп заявок можем использовать тики.

    // Алгоритм:
    // Строится два канала Дончиана - ChannelOne и ChannelTwo. Канал Дончиана - это пара индикаторов Highest и Lowest.
    // Период внутреннего канала ChannelTwo устанавливаем в процентах относительно внешнего канала ChannelOne.
    // Входим в позицию:
    // - Покупаем, когда цена касается канала UpperChannelOne.
    // - Продаем, когда цена касается канала LowerChannelOne.
    // Закрываем позицию:
    // - Покупаем, когда цена касается канала UpperChannelTwo.
    // - Продаем, когда цена касается канала LowerChannelTwo.

    // Дополнительно:
    // 1) Стратегия интрадей. Торгуем с 10:05, в конце торговой сессии (23:40) закрываем все открытые позиции - метод CheckIntraDayTime().
    // 2) При входе/выходе ориентируемся на риск, который рассчитываем по формуле: risk = k * atr,
    // Где к - коэффициент риска, atr - текущее значение индикатора ATR.
    // 3) Для расчета стоп заявок используем соответствующие методы MarketToMarket для тиков и свечек.
    // 4) Объем входа в позицию рассчитываем по формуле:
    // Volume = Math.Min(currentFunds * riskPerTrade / risk, currentFunds / initialMargin), 
    // Где currentFunds - текущие доступные денежные средства, initialMargin - гарантийное обеспечение
    // riskPerTrade - процент риска, risk - риск (см. выше).
    // 5) Все лимитные заявки выставляем "глубоко в рынок" для мгновенного исполнения как маркет.

    // Индикаторы:
    // Две пары индикаторов Highest и Lowest - два канала Дончиана.
    // Индикатор ATR (Average True Range) - для расчета риска.

    public class TurtleTraders : IStrategy
    {
        private Highest _upperChannelOne;
        private Lowest _lowerChannelOne;
        private Highest _upperChannelTwo;
        private Lowest _lowerChannelTwo;
        private ATR _atr;

        private int _k;
        private decimal _riskPerTrade;

        private Candle _lastCandle;
        private decimal _risk;
        private decimal _entryPrice;
        private bool _filled = true;
        private decimal _offset;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Создаем индикаторы для торговой стратегии

                //Период первого канала
                var periodDonchianChannelOne = (int) Parameter(1);
                _upperChannelOne = new Highest(periodDonchianChannelOne);
                _lowerChannelOne = new Lowest(periodDonchianChannelOne);

                //Период второго канала - процент от периода от первого канала
                var periodDonchianChannelTwo = (int) (periodDonchianChannelOne * Parameter(2) * 0.01m);
                _upperChannelTwo = new Highest(periodDonchianChannelTwo);
                _lowerChannelTwo = new Lowest(periodDonchianChannelTwo);

                //Для расчета риска и объема позиции
                _atr = new ATR((int)Parameter(3));
                _k = (int)Parameter(4);
                _riskPerTrade = Parameter(5) * 0.01m;

                //Для выставления лимитных заявок в рынок для гарантированного исполнения
                _offset = GetSecurity().Tick * 10;

                //Подписываемся на события при инициализации стратегии
                Subscribe();
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "Strategy");
            }
        }

        //Подписываемся на события при инициализации стратегии
        private void Subscribe()
        {
            //При остановке старегии сбрасываем индикаторы
            StrategyStateChanged += state =>
            {
                if (state == StrategyState.NotActivated)
                    ResetIndicators();
            };

            //Два варианта работы стратегии - на тиках и свечках 
            if (HistoricalDataType == HistoricalDataType.Ticks || StrategyMode == StrategyMode.Trading)
            {
                NewCandle += ProcessCandleTicks;
                NewTick += ProcessTick;
            }
            else
            {
                NewCandle += ProcessOnlyCandle;
            }
        }

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("Period Highest/Lowest for Entry", 100, 50, 150, 10) {Comment = "Period for Donchian Channel One"},
                new Parameter("Percentage of the period Entry for Exit, %", 50) {Comment = "Percentage period for Donchian Channel Two"},
                new Parameter("Period SMA for ATR", 30, 20, 40, 5),
                new Parameter("Сoefficient K", 2),
                new Parameter("Risk percentage trade, %", 2, 1, 4, 1)
            };
        }

        //Индикаторы для отрисовки в Analyzer
        public override List<BaseAnalyzerIndicator> AnalyzerIndicators()
        {
            return new List<BaseAnalyzerIndicator>
            {
                new AnalyzerIndicator(new Highest((int) Parameter(1)), AnalyzerValue.Candle, 0)
                {
                    Name = "UpperChannelOne",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.OrangeRed,
                    Thickness = 3
                },

                new AnalyzerIndicator(new Lowest((int) Parameter(1)), AnalyzerValue.Candle, 0)
                {
                    Name = "LowerChannelOne",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.DeepSkyBlue,
                    Thickness = 3
                },

                new AnalyzerIndicator(new Highest((int) (Parameter(2)*Parameter(1)*0.01m)), AnalyzerValue.Candle, 0)
                {
                    Name = "UpperChannelTwo",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Red,
                    Thickness = 2
                },

                new AnalyzerIndicator(new Lowest((int) (Parameter(2)*Parameter(1)*0.01m)), AnalyzerValue.Candle, 0)
                {
                    Name = "LowerChannelTwo",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Blue,
                    Thickness = 2
                },

                new AnalyzerIndicator(new ATR((int) Parameter(3)), AnalyzerValue.Candle, 1)
                {
                    Name = "ATR",
                    Style = IndicatorStyle.Histogram,
                    Stroke = Colors.Green,
                    Thickness = 2
                }
            };
        }

        //Сброс индикаторов
        private void ResetIndicators()
        {
            _upperChannelOne.Reset();
            _lowerChannelOne.Reset();
            _upperChannelTwo.Reset();
            _lowerChannelTwo.Reset();
            _atr.Reset();
        }


        //Test on Ticks
        private void ProcessCandleTicks(Candle candle)
        {
            try
            {
                _upperChannelOne.Add(candle);
                _lowerChannelOne.Add(candle);
                _upperChannelTwo.Add(candle);
                _lowerChannelTwo.Add(candle);
                _atr.Add(candle);
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessCandleTicks");
            }
        }

        private void ProcessTick(Trade tick)
        {
            try
            {
                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_upperChannelOne.IsFormed || !_lowerChannelOne.IsFormed || !_upperChannelTwo.IsFormed || !_lowerChannelTwo.IsFormed || !_atr.IsFormed) return;

                //Текущая позиция по выбранному коннектору и инструменту
                var position = GetTradeInfo().Position;

                //Доступные денежные средства стратегии
                var currentFunds = GetStrategyInfo().CurrentFunds;

                //Цена последнего тика
                var lastPrice = GetLastTick().Price;

                //Check intraday
                if (!CheckIntraDayTime(tick.Time.TimeOfDay, position, lastPrice)) return;

                //Stop-Loss
                MarketToMarketTicks(tick, position, lastPrice);

                //Entry
                if (tick.Price > _upperChannelOne.LastValue && position == 0 && _filled)
                {
                    _risk = _k * _atr.LastValue;

                    var volume = _risk <= 0 ? (int)(currentFunds / GetSecurity().InitialMargin) : (int)Math.Min(currentFunds * _riskPerTrade / _risk, currentFunds / GetSecurity().InitialMargin);

                    if (volume == 0) volume = 1;

                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = volume,
                        Comment = "Entry Buy"
                    };

                    order.OrderChanged += newOrder =>
                    {
                        _filled = newOrder.State == OrderState.Filled;
                        if (newOrder.State == OrderState.Filled) _entryPrice = newOrder.Price;
                    };

                    RegisterOrder(order);
                }
                else if (tick.Price < _lowerChannelOne.LastValue && position == 0 && _filled)
                {
                    _risk = _k * _atr.LastValue;

                    var volume = _risk <= 0 ? (int)(currentFunds / GetSecurity().InitialMargin) : (int)Math.Min(currentFunds * _riskPerTrade / _risk, currentFunds / GetSecurity().InitialMargin);

                    if (volume == 0) volume = 1;

                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = volume,
                        Comment = "Entry Sell"
                    };

                    order.OrderChanged += newOrder =>
                    {
                        _filled = newOrder.State == OrderState.Filled;
                        if (newOrder.State == OrderState.Filled) _entryPrice = newOrder.Price;
                    };

                    RegisterOrder(order);
                }

                //Exit
                if (tick.Price < _lowerChannelTwo.LastValue && position > 0 && _filled)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = position,
                        Comment = "Exit Sell"
                    };

                    order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                    RegisterOrder(order);
                }
                else if (tick.Price > _upperChannelTwo.LastValue && position < 0 && _filled)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = -position,
                        Comment = "Exit Buy"
                    };

                    order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                    RegisterOrder(order);
                }
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessTick");
            }
        }

        private void MarketToMarketTicks(Trade tick, decimal position, decimal lastPrice)
        {
            try
            {
                if (position > 0 && _entryPrice - tick.Price > _risk && _filled)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = position,
                        Comment = "Exit Stop Sell"
                    };

                    order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                    RegisterOrder(order);
                }
                else if (position < 0 && tick.Price - _entryPrice > _risk && _filled)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = -position,
                        Comment = "Exit Stop Buy "
                    };

                    order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                    RegisterOrder(order);
                }
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "MarketToMarketTicks");
            }
        }


        //Test only on Candles
        private void ProcessOnlyCandle(Candle candle)
        {
            try
            {
                //Формируем индикаторы с задержкой на одну свечку
                if (_lastCandle != null)
                {
                    _upperChannelOne.Add(_lastCandle);
                    _lowerChannelOne.Add(_lastCandle);
                    _upperChannelTwo.Add(_lastCandle);
                    _lowerChannelTwo.Add(_lastCandle);
                    _atr.Add(candle);
                }

                _lastCandle = candle;

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_upperChannelOne.IsFormed || !_lowerChannelOne.IsFormed || !_upperChannelTwo.IsFormed || !_lowerChannelTwo.IsFormed || !_atr.IsFormed) return;

                //Текущая позиция по выбранному коннектору и инструменту
                var position = GetTradeInfo().Position;

                //Текущие доступные денежные средства доступные стратегии
                var currentFunds = GetStrategyInfo().CurrentFunds;

                //Цена последней свечки
                var lastPrice = GetLastCandle().ClosePrice;

                if (!CheckIntraDayTime(candle.CloseTime.TimeOfDay, position, lastPrice)) return;

                //Stop-Loss
                MarketToMarketCandle(candle, position, lastPrice);

                //Entry
                if (candle.HighPrice > _upperChannelOne.LastValue && position == 0)
                {
                    _risk = _k * _atr.LastValue;

                    var volume = (int)Math.Min(currentFunds * _riskPerTrade / _risk, currentFunds / GetSecurity().InitialMargin);

                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = volume,
                        Comment = "Entry Buy"
                    };

                    order.OrderChanged += newOrder => { if (newOrder.State == OrderState.Filled) _entryPrice = newOrder.Price; };

                    RegisterOrder(order);
                }
                else if (candle.LowPrice < _lowerChannelOne.LastValue && position == 0)
                {
                    _risk = _k * _atr.LastValue;

                    var volume = (int)Math.Min(currentFunds * _riskPerTrade / _risk, currentFunds / GetSecurity().InitialMargin);

                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = volume,
                        Comment = "Entry Sell"
                    };

                    order.OrderChanged += newOrder => { if (newOrder.State == OrderState.Filled) _entryPrice = newOrder.Price; };

                    RegisterOrder(order);
                }

                //Exit
                if (candle.LowPrice < _lowerChannelTwo.LastValue && position > 0)
                    RegisterOrder(new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = position,
                        Comment = "Exit Sell"
                    });
                else if (candle.HighPrice > _upperChannelTwo.LastValue && position < 0)
                    RegisterOrder(new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = -position,
                        Comment = "Exit Buy"
                    });
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessOnlyCandle");
            }
        }

        private void MarketToMarketCandle(Candle candle, decimal position, decimal lastPrice)
        {
            try
            {
                if (position > 0 && _entryPrice - candle.LowPrice > _risk)
                    RegisterOrder(new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Price = lastPrice - _offset,
                        Volume = position,
                        Comment = "Exit Stop Sell"
                    });
                else if (position < 0 && candle.HighPrice - _entryPrice > _risk)
                    RegisterOrder(new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Price = lastPrice + _offset,
                        Volume = -position,
                        Comment = "Exit Stop Buy"
                    });
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "MarketToMarketCandle");
            }
        }


        //Check intraday
        private bool CheckIntraDayTime(TimeSpan timeOfDay, decimal position, decimal lastPrice)
        {
            try
            {
                if (timeOfDay < new TimeSpan(0, 10, 05, 0)) return false;

                if (timeOfDay >= new TimeSpan(0, 23, 40, 0) && timeOfDay <= new TimeSpan(0, 23, 45, 0))
                {
                    if (position > 0 && _filled)
                    {
                        var order = new Order
                        {
                            Type = OrderType.Limit,
                            Direction = Direction.Sell,
                            Price = lastPrice - _offset,
                            Volume = position,
                            Comment = "Exit Sell EndDay"
                        };

                        order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                        RegisterOrder(order);
                    }

                    if (position < 0 && _filled)
                    {
                        var order = new Order
                        {
                            Type = OrderType.Limit,
                            Direction = Direction.Buy,
                            Price = lastPrice + _offset,
                            Volume = -position,
                            Comment = "Exit Buy EndDay"
                        };

                        order.OrderChanged += newOrder => _filled = newOrder.State == OrderState.Filled;
                        RegisterOrder(order);
                    }

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "CheckIntraDayTime");
                return false;
            }
        }

        //Обработка ошибок
        private void ExceptionMessage(Exception ex, string title)
        {
            //Отправляем сообщение с ошибкой в лог
            MessageToLog($"Exeption: {title}\n{ex}");

            //Останавливаем стратегию
            Stop();
        }
    }
}