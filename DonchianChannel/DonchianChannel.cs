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
using SAT.Algo.Orders;
using SAT.Indicators;
using SAT.Trading;

namespace DonchianChannel
{
    // Описание:
    // За основу взята стратегия на каналах Дончиана - http://www.oxfordstrat.com/trading-strategies/adx

    // Исходные данные: свечки или тики.
    // Свечки используются для расчета индикаторов.
    // Для более точного расчета алгоритмических заявок в качестве исходных данных можно использовать тики.

    // Алгоритм:
    // Строится два канала Дончиана - ChannelOne и ChannelTwo.  Канал Дончиана - это пара индикаторов Highest и Lowest.
    // Период внутреннего канала ChannelTwo устанавливваем в 2 раза меньше периода внешнего канала ChannelOne.
    // Входим в позицию:
    // - Покупаем, когда цена касается канала UpperChannelOne.
    // - Продаем, когда цена касается канала LowerChannelOne.
    // Закрываем позицию:
    // - Покупаем, когда цена касается канала UpperChannelTwo.
    // - Продаем, когда цена касается канала LowerChannelTwo.

    // Дополнительно:
    // 1) Фильтр - два индикатора ADX. Торгуем только если ADX_Look_Back > ADX_Threshold.
    // 2) По индикатору ATR определяем стоп цену для стоп заявки (stopPrice = newOrder.Price - atr*AtrStop).
    // 3) Все лимитные заявки выставляем "глубоко в рынок" для мгновенного исполнения как маркет.

    // Индикаторы:
    // Две пары индикаторов Highest и Lowest - два канала Дончиана.
    // Два индикатора ADX (Average Directional Index) _adxLookBack и _adxThreshold - фильтры.
    // Индикатор ATR (Average True Range) - для расчета стоп цены.

    // Опции: 
    // - Защита позиции алгоритмической стоп заявкой с выставлением лимитных заявок глубоко в стакан для исполнения как рыночных.

    public class DonchianChannel : IStrategy
    {
        private Highest _upperChannelOne;
        private Lowest _lowerChannelOne;
        private Highest _upperChannelTwo;
        private Lowest _lowerChannelTwo;
        private ADX _adxLookBack;
        private ADX _adxThreshold;
        private ATR _atr;

        private int _atrStop;
        private int _volume;
        private decimal _offset;

        private AlgoStopOrders _algoStopOrders;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Создаем индикаторы для торговой стратегии
                _upperChannelOne = new Highest((int) Parameter(1));
                _lowerChannelOne = new Lowest((int) Parameter(1));
                _upperChannelTwo = new Highest((int) Parameter(1)/2); //50% от P1
                _lowerChannelTwo = new Lowest((int) Parameter(1)/2); //50% от P1

                _adxLookBack = new ADX((int) Parameter(2));
                _adxThreshold = new ADX((int) Parameter(3));

                _atr = new ATR((int) Parameter(4));
                _atrStop = (int)Parameter(5);

                _volume = (int) Parameter(6);

                //Для выставления лимитных заявок в рынок для гарантированного исполнения
                _offset = GetSecurity().Tick*50;

                //Инициализируем алгоритмические заявки
                InitializationAlgoOrders();

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

            //Подписываемся на свечки
            NewCandle += candle =>
            {
                //Если мы тестируем на свечках, рассчитываем алгоритмические стоп заявки по свечкам
                if (HistoricalDataType == HistoricalDataType.Candles)
                    _algoStopOrders.Add(candle);

                ProcessCandle(candle);
            };

            //Если торгуем или тестируем на тиках, рассчитываем алгоритмические стоп заявки по тикам
            NewTick += _algoStopOrders.Add;
        }

        //Инициализируем алгоритмические заявки
        private void InitializationAlgoOrders()
        {
            //Используем алгоритмические стоп заявки
            _algoStopOrders = new AlgoStopOrders();
            //Выводим в лог сообщение об активации алгоритмической стоп заявки
            _algoStopOrders.Activated += (id, stopPrice) => MessageToLog($"Activated Stop Order ID{id} - Stop Price {stopPrice}");
            //Выводим в лог сообщение о снятии алгоритмической стоп заявки
            _algoStopOrders.Cancelled += (stopPrice, id) => MessageToLog($"Cancelled Stop Order ID{id} - StopPrice {stopPrice}");
            //Выводим в лог сообщение об изменеии трейлинг стоп заявки
            _algoStopOrders.TrailingPriceChanged += (id, stopPrice, price) => MessageToLog($"Trailing Price Changed Stop Order ID{id} - stop price {stopPrice}, order price {price}");
            //Алгоритмическая стоп-заявка исполнилась
            _algoStopOrders.Filled += (price, volume, direction, comment, id) =>
            {
                MessageToLog($"Filled Stop Order ID{id} - {direction}, {comment}");

                //Создаем лимитную заявку для закрытия позиции
                var limitOrder = new Order
                {
                    Type = OrderType.Limit,
                    Direction = direction,
                    Volume = volume,
                    Price = price,
                    Comment = comment
                };

                //Закрываем позицию
                RegisterOrder(limitOrder);
            };
        }

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("Period UpperChannelOne/LowerChannelOne", 100, 50, 150, 10) {Comment = "Period for Donchian Channels"},
                new Parameter("Period ADX_Look_Back", 14) {Comment = "Filter"},
                new Parameter("Period ADX_Threshold", 30) {Comment = "Filter"},
                new Parameter("Period ATR", 20, 15, 25, 1) {Comment = "For calculating stop price"},
                new Parameter("AtrStop", 6) {Comment = "Constant for calculating stop price"},
                new Parameter("Volume", 1) {Comment = "Order volume"}
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

                new AnalyzerIndicator(new Highest((int) Parameter(1)/2), AnalyzerValue.Candle, 0)
                {
                    Name = "UpperChannelTwo",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Red,
                    Thickness = 2
                },

                new AnalyzerIndicator(new Lowest((int) Parameter(1)/2), AnalyzerValue.Candle, 0)
                {
                    Name = "LowerChannelTwo",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Blue,
                    Thickness = 2
                },

                new AnalyzerIndicator(new ADX((int) Parameter(2)), AnalyzerValue.Candle, 1)
                {
                    Name = "LookBack",
                    Style = IndicatorStyle.Mountain,
                    Stroke = Colors.DeepSkyBlue,
                    Thickness = 2
                },

                new AnalyzerIndicator(new ADX((int) Parameter(3)), AnalyzerValue.Candle, 1)
                {
                    Name = "Threshold",
                    Style = IndicatorStyle.Mountain,
                    Stroke = Colors.Blue,
                    Thickness = 2
                },

                new AnalyzerIndicator(new ATR((int) Parameter(4)), AnalyzerValue.Candle, 1)
                {
                    Name = "ATR",
                    Style = IndicatorStyle.Mountain,
                    Stroke = Colors.DarkBlue,
                    Thickness = 2
                },
            };
        }

        //Сброс индикаторов
        private void ResetIndicators()
        {
            _upperChannelOne.Reset();
            _lowerChannelOne.Reset();
            _lowerChannelTwo.Reset();
            _adxLookBack.Reset();
            _adxThreshold.Reset();
            _atr.Reset();
        }

        //Логика торговой стратегии
        public void ProcessCandle(Candle candle)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var upperChannelOne = _upperChannelOne.Add(candle);
                var lowerChannelOne = _lowerChannelOne.Add(candle);
                var upperChannelTwo = _upperChannelTwo.Add(candle);
                var lowerChannelTwo = _lowerChannelTwo.Add(candle);
                var adxLookBack = _adxLookBack.Add(candle);
                var adxThreshold = _adxThreshold.Add(candle);
                var atr = _atr.Add(candle);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы еще не сформировались, то ждем пока сформируются
                if (!_upperChannelOne.IsFormed || !_lowerChannelOne.IsFormed || !_upperChannelTwo.IsFormed || !_lowerChannelTwo.IsFormed ||
                    !_adxLookBack.IsFormed || !_adxThreshold.IsFormed || !_atr.IsFormed) return;

                //Фильтр. Торгуем только если ADX_Look_Back > ADX_Threshold.
                if (adxLookBack <= adxThreshold) return;

                //Текущая позиция по выбранному коннектору и инструменту
                var position = GetTradeInfo().Position;

                //Берем цену последней сделки как цену для заявки
                var price = HistoricalDataType == HistoricalDataType.Candles ? GetLastCandle().ClosePrice : GetLastTick().Price;

                //Входим в позицию. Покупаем, когда цена касается канала UpperChannelOne.
                if (candle.HighPrice >= upperChannelOne && position == 0)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Volume = _volume,
                        Price = price + _offset
                    };

                    //Подписываемся на событие изменения заявки
                    order.OrderChanged += newOrder =>
                    {
                        //Текущая позиция по выбранному коннектору и инструменту
                        var pos = GetTradeInfo().Position;

                        //Если заявка исполнилась и мы вошли в позицию, то защищаем ее алгоритмической стоп заявкой
                        if (newOrder.State == OrderState.Filled && pos != 0)
                        {
                            var stopPrice = newOrder.Price - atr*_atrStop;
                            var stopOrderId = _algoStopOrders.Activate(order.Volume, order.Direction, stopPrice, stopPrice - _offset, false);
                            MessageToLog($"Created algorithmic stop order ID{stopOrderId} - stop price {stopPrice}, order price {stopPrice - _offset}");
                        }
                    };

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(order);
                }

                //Входим в позицию. Продаем, когда цена касается канала LowerChannelOne.
                else if (candle.LowPrice <= lowerChannelOne && position == 0)
                {
                    var order = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Volume = _volume,
                        Price = price - _offset
                    };

                    //Подписываемся на событие изменения заявки
                    order.OrderChanged += newOrder =>
                    {
                        //Текущая позиция по выбранному коннектору и инструменту
                        var pos = GetTradeInfo().Position;

                        //Если заявка исполнилась и мы вошли в позицию, то защищаем ее Стоп-лимит заявкой
                        if (newOrder.State == OrderState.Filled && pos != 0)
                        {
                            var stopPrice = newOrder.Price + atr*_atrStop;
                            var stopOrderId = _algoStopOrders.Activate(order.Volume, order.Direction, stopPrice, stopPrice + _offset, false);
                            MessageToLog($"Create algorithmic stop order ID{stopOrderId} - stop price {stopPrice}, order price {stopPrice + _offset}");
                        }
                    };

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(order);
                }

                //Закрываем позиции. Продаем, когда цена касается канала LowerChannelTwo.
                else if (candle.LowPrice <= lowerChannelTwo && position > 0)
                {
                    var limitOrder = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Volume = Math.Abs(position),
                        Price = price - _offset
                    };

                    //Снимаем все лимитные заявки
                    CancelOrders();
                    //Снимаем все алгоритмические стоп заявки
                    _algoStopOrders.CancelAllOrders();

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(limitOrder);
                }

                //Закрываем позиции. Покупаем, когда цена касается канала UpperChannelTwo.
                else if (candle.HighPrice >= upperChannelTwo && position < 0)
                {
                    var limitOrder = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Volume = Math.Abs(position),
                        Price = price + _offset
                    };

                    //Снимаем все лимитные заявки
                    CancelOrders();
                    //Снимаем все алгоритмические стоп заявки
                    _algoStopOrders.CancelAllOrders();

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(limitOrder);
                }
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessCandle");
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