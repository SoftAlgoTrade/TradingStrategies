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

namespace SimpleMovingAverage
{
    // Описание:
    // Стратегия работает на пересечении двух простых скользящих средних.

    // Исходные данные: свечки или тики.
    // Свечки используются для расчета индикаторов.
    // Для более точного расчета алгоритмических заявок в качестве исходных данных можно использовать тики.

    // Алгоритм:
    // Когда "длинная" скользящая средняя пересекает "короткую" - продаем, наоборот - покупаем.

    // Выставляем лимитные заявки "глубоко в рынок" для мгновенного исполнения как маркет.

    // Индикаторы SMA (Simple Moving Average):
    // Long ("длинная") - SMA с большим периодом.
    // Short ("короткая") - SMA с меньшим периодом.

    // Опции: 
    // - Защита позиции алгоритмической стоп заявкой с выставлением лимитных заявок глубоко в стакан для исполнения как рыночных.
    // - Алгоритмическое снятие лимитных заявок по таймеру в случае неисполнения за указанный период времени.

    public class SimpleMovingAverage : IStrategy
    {
        private SMA _longSma;
        private SMA _shortSma;
        private decimal _lastSma;

        private int _volume;
        private decimal _offset;
        private decimal _stopOrderOffset;
        private bool _isTrailing;

        private AlgoStopOrders _algoStopOrders;
        private AlgoCancelOrderByTimer _algoCancelOrdersByTimer;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Создаем индикаторы для торговой стратегии
                _longSma = new SMA((int)Parameter(1));
                _shortSma = new SMA((int)Parameter(2));

                //Отступ для стоп заявки
                _stopOrderOffset = Parameter(3);

                //Тип стоп заявки - трейлинг (1 - True, 0 - False)
                _isTrailing = (int)Parameter(4) == 1;

                //Величина смещения цены для исполнения лимитных заявок как рыночных
                _offset = GetSecurity().Tick * (int)Parameter(5);

                //Объем заявки
                _volume = (int)Parameter(6);

                //Подписываемся на события при инициализации стратегии
                Subscribe();

                //Инициализируем алгоритмические заявки
                InitializationAlgoOrders();
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
                {
                    _longSma.Reset();
                    _shortSma.Reset();
                }
            };

            //Подписываемся на свечки
            NewCandle += candle =>
            {
                //Если мы тестируем на свечках, рассчитываем алгоритмические заявки по свечкам
                if (HistoricalDataType == HistoricalDataType.Candles)
                {
                    _algoCancelOrdersByTimer.Add(candle);
                    _algoStopOrders.Add(candle);
                }

                ProcessCandle(candle);
            };

            //Подписываемся на тики
            //Если торгуем или тестируем на тиках, рассчитываем алгоритмические стоп заявки по тикам (результаты получаются точнее, чем на свечках)
            NewTick += tick =>
            {
                _algoCancelOrdersByTimer.Add(tick);
                _algoStopOrders.Add(tick);
            };

            //Если возникли ошибки с заявками, то останавливаем стратегию 
            OrderChanged += order =>
            {
                if (order.State == OrderState.Failed)
                {
                    //Отправляем сообщение в лог и останавливаем стратегию
                    MessageToLog($"Order ID{order.OrderId} failed. Strategy stopped.");
                    Stop();
                }
            };
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

            //Используем алгоритмическое снятие лимитных заявок по таймеру
            _algoCancelOrdersByTimer = new AlgoCancelOrderByTimer(this, TimeSpan.FromMilliseconds(TimeFrame.TotalMilliseconds * (int)Parameter(7)));
            //Выводим в лог сообщение о деактивации алгоритмической заявки для снятия лимитной заявки по таймеру
            _algoCancelOrdersByTimer.Cancelled += orderId => MessageToLog($"Algo cancel order by timer ID{orderId} deactivated");
        }

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("Long", 100, 80, 400, 10) {Comment = "Period Long/Slow SMA indicator"},
                new Parameter("Short", 50, 10, 100, 5) {Comment = "Period Short/Fast SMA indicator"},
                new Parameter("Stop order offset", 50, 20, 70, 10) {Comment = "Stop order offset in points"},
                new Parameter("Stop order is trailing", 0) {Comment = "1 - True, 0 - False"},
                new Parameter("Offset for execution limit orders as market", 5) {Comment = "Example: Offset = Security.Tick * 5, 5 - value"},
                new Parameter("Volume", 1) {Comment = "Order volume"},
                new Parameter("Algo cancellation timer", 3) {Comment = "Example: Timer = TimeSpan.FromMilliseconds(TimeFrame.TotalMilliseconds * 3), 3 - value"}
            };
        }

        //Индикаторы для отрисовки в Analyzer
        public override List<BaseAnalyzerIndicator> AnalyzerIndicators()
        {
            return new List<BaseAnalyzerIndicator>
            {
                new AnalyzerIndicator(new SMA((int) Parameter(1)), AnalyzerValue.CandleClosePrice, 0)
                {
                    Name = "Long",
                    Stroke = Colors.Green,
                    Thickness = 2
                },

                new AnalyzerIndicator(new SMA((int) Parameter(2)), AnalyzerValue.CandleClosePrice, 0)
                {
                    Name = "Short",
                    Stroke = Colors.Red,
                    Thickness = 2
                }
            };
        }

        //Логика торговой стратегии
        private void ProcessCandle(Candle candle)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var shortSma = _shortSma.Add(candle.ClosePrice);
                var longSma = _longSma.Add(candle.ClosePrice);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_shortSma.IsFormed || !_longSma.IsFormed) return;

                //Рассчитываем положение скользящих стредних друг относительно друга
                var stateSma = shortSma - longSma;

                //Текущая позиция по выбранному коннектору и инструменту
                var position = GetTradeInfo().Position;

                //Если короткая SMA больше или равна длинной SMA
                //и предыдущее положение было обратным, то короткая пересекла длинную SMA - Покупаем!
                if (stateSma >= 0 && _lastSma < 0 && position <= 0)
                {
                    var limitOrder = GetLimitOrder(Direction.Buy, position, candle.ClosePrice);

                    //Подписываемся на событие изменения заявки
                    limitOrder.OrderChanged += order =>
                    {
                        //Текущая позиция по выбранному коннектору и инструменту
                        var pos = GetTradeInfo().Position;

                        //Если заявка исполнилась, то защищаем ее алгоритмической стоп заявкой
                        if (order.State == OrderState.Filled && pos != 0)
                        {
                            var stopOrderId = _algoStopOrders.Activate(order.Volume, order.Direction, order.Price - _stopOrderOffset, order.Price - _stopOrderOffset - _offset, _isTrailing);
                            MessageToLog($"Algo stop order ID{stopOrderId} - stop price {order.Price - _stopOrderOffset}, order price {order.Price - _stopOrderOffset - _offset}");
                        }

                        //Обрабатываем алгоритмические заявки
                        CheckAlgoOrders(order, pos);
                    };

                    //Создаем защитную алгоритмическую заявку на снятие лимитной заявки по таймеру
                    var cancelOrderId = _algoCancelOrdersByTimer.Create(limitOrder);
                    MessageToLog($"Algo cancel by timer ID{cancelOrderId} - waiting time {_algoCancelOrdersByTimer.Timer.TotalSeconds} sec");

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(limitOrder);
                }

                //Если короткая SMA меньше или равна длинной SMA
                //и предыдущее положение было обратным, то длинная пересекла короткую SMA - Продаем!
                if (stateSma <= 0 && _lastSma > 0 && position >= 0)
                {
                    var limitOrder = GetLimitOrder(Direction.Sell, position, candle.ClosePrice);

                    //Подписываемся на событие изменения заявки
                    limitOrder.OrderChanged += order =>
                    {
                        //Текущая позиция по выбранному коннектору и инструменту
                        var pos = GetTradeInfo().Position;

                        //Если заявка исполнилась, то защищаем ее алгоритмической стоп заявкой
                        if (order.State == OrderState.Filled && pos != 0)
                        {
                            var stopOrderId = _algoStopOrders.Activate(order.Volume, order.Direction, order.Price + _stopOrderOffset, order.Price + _stopOrderOffset + _offset, _isTrailing);
                            MessageToLog($"Algo stop order ID{stopOrderId} - stop price {order.Price + _stopOrderOffset}, order price {order.Price + _stopOrderOffset + _offset}");
                        }

                        //Обрабатываем алгоритмические заявки
                        CheckAlgoOrders(order, pos);
                    };

                    //Создаем защитную алгоритмическую заявку на снятие лимитной заявки по таймеру
                    var cancelOrderId = _algoCancelOrdersByTimer.Create(limitOrder);
                    MessageToLog($"Algo cancel by timer ID{cancelOrderId} - waiting time {_algoCancelOrdersByTimer.Timer.TotalSeconds} sec");

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(limitOrder);
                }

                //Запоминаем текущее положение SMA относительно друг друга
                _lastSma = stateSma;

            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessCandle");
            }
        }

        //Создаем лимитную заявку
        private Order GetLimitOrder(Direction direction, decimal position, decimal price)
        {
            return new Order
            {
                Type = OrderType.Limit,
                Direction = direction,
                Volume = GetVolume(direction, position),
                Price = price
            };
        }

        //Рассчитываем объем заявки
        private decimal GetVolume(Direction direction, decimal position)
        {
            if (position < 0 && direction == Direction.Buy) return -position;
            if (position > 0 && direction == Direction.Sell) return position;

            return _volume;
        }

        //Обработка алгоритмических заявок
        private void CheckAlgoOrders(Order order, decimal pos)
        {
            //Снимаем все алгоритмические стоп заявки, если вышли из позиции и по условиям состояния заявки
            if ((order.State == OrderState.Filled && pos == 0) || order.State == OrderState.Cancelled || order.State == OrderState.Failed)
                _algoStopOrders.CancelAllOrders();

            //Снимаем все алгоритмические заявки на снятие лимитных заявок по таймеру по условиям состояния заявки
            if (order.State == OrderState.Filled || order.State == OrderState.Cancelled || order.State == OrderState.Failed)
                _algoCancelOrdersByTimer.CancelAllOrders();
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