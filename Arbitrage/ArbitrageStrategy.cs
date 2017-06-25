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
using SAT.Trading;

namespace Arbitrage
{
    // Описание:
    // Классическая арбитражная стратегия
    // Покупка и продажа спреда пары спот-фьючерс на основе индикатора Bollinger Bands

    // Исходные данные: свечки или тики. Рассмотрены оба варианта.

    // Алгоритм:
    // Спред пересекает канал Боллинджера снизу вверх - продаем спред, сверху вниз - покупаем спред.
    // При пересечении средней линии Боллинджера выходим из позиции.

    // Выставляем лимитные заявки "глубоко в рынок" для мгновенного исполнения как маркет.

    // Индикаторы:
    // Линии Боллинджера - Top, Average, Bottom
    // Усредненное значение спреда - Average Spread (SMA)

    // Дополнительные индикаторы:
    // Из-за разности масштабов графиков на первый чарт дополнительно выводятся индикаторы с ценой по каждому инструменту - Futures Price, Stock Price.
    // Можно переключать видимость между основной серией и каждой в отдельности через легенду на графике.
    // Во второй чарт для визуального анализа выводятся значения спреда по каждому инструменту - Futures Spread, Stock Spread.
    // Для разных типов данных используются индикаторы с соответствующими приставками в названии - OnTicks/OnCandles.

    // Пример (Газпром):

    // Акция:
    // Код      Code = GAZP@GAZP_Stock
    // Лот      LotSize = 10
    // Шаг цены Tick = 0.01
    // Объем    Volume = 10

    // Фьючерс:
    // Код      Code = GAZR@FORTS
    // Лот      LotSize = 1
    // Шаг цены Tick = 1
    // Объем    Volume = 1
    // ГО       InitialMargin = 2000 (только у фьючерсов, у акций гарантийное обеспечение всегда должно быть равно 0!)

    // Внимание! Обязательно прописываем вышеуказанные параметры в настройках инструментов!

    public class ArbitrageStrategy : IStrategy
    {
        //Индикаторы работающие на тиках 
        private BollingerTopOnTicks _topOnTicks;
        private BollingerAverageOnTicks _averageOnTicks;
        private BollingerBottomOnTicks _bottomOnTicks;
        private AverageSpreadOnTicks _averageSpreadOnTicks;

        //Индикаторы работающие на свечках
        private BollingerTopOnCandles _topOnCandles;
        private BollingerAverageOnCandles _averageOnCandles;
        private BollingerBottomOnCandles _bottomOnCandles;
        private AverageSpreadOnCandles _averageSpreadOnCandles;

        private ConnectorName _conName;

        private int _volumeStocks;
        private int _volumeFutures;

        private Security _futures;
        private Security _stock;

        private decimal _offsetFutures;
        private decimal _offsetStock;

        private bool _buySpread;
        private bool _sellSpread;

        private bool _waitFilled;

        private bool _filledFutures;
        private bool _filledStock;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Выбираем коннектор
                _conName = ConnectorNames[0];

                //Выбираем инструменты
                var securities = Securities[_conName];
                foreach (var security in securities)
                {
                    if (security.InitialMargin > 0)
                        _futures = security;
                    else
                        _stock = security;
                }

                //Создаем индикаторы для торговой стратегии
                if (HistoricalDataType == HistoricalDataType.Candles)
                {
                    _averageSpreadOnCandles = new AverageSpreadOnCandles((int) Parameter(1));
                    _topOnCandles = new BollingerTopOnCandles((int) Parameter(2), (int) Parameter(3));
                    _averageOnCandles = new BollingerAverageOnCandles((int) Parameter(2));
                    _bottomOnCandles = new BollingerBottomOnCandles((int) Parameter(2), (int) Parameter(3));
                }
                else
                {
                    _averageSpreadOnTicks = new AverageSpreadOnTicks((int) Parameter(1));
                    _topOnTicks = new BollingerTopOnTicks((int) Parameter(2), (int) Parameter(3));
                    _averageOnTicks = new BollingerAverageOnTicks((int) Parameter(2));
                    _bottomOnTicks = new BollingerBottomOnTicks((int) Parameter(2), (int) Parameter(3));
                }

                //Величина смещения цены для исполнения лимитных заявок как рыночных
                _offsetFutures = _futures.Tick * (int)Parameter(4);
                _offsetStock = _stock.Tick * (int)Parameter(4);

                //Объем заявки
                _volumeStocks = (int) Parameter(5);
                _volumeFutures = (int) Parameter(6);

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
            //Если тестируем на свечках подписываемся на свечки
            //Если торгуем или тестируем на тиках подписываемся на тики
            if (HistoricalDataType == HistoricalDataType.Candles)
                NewCandle += ProcessOnCandles;
            else
                NewTick += ProcessOnTicks;

            //При остановке старегии сбрасываем индикаторы
            StrategyStateChanged += state =>
            {
                if (state == StrategyState.NotActivated)
                {
                    if (HistoricalDataType == HistoricalDataType.Candles)
                    {
                        _averageSpreadOnCandles.Reset();
                        _topOnCandles.Reset();
                        _averageOnCandles.Reset();
                        _bottomOnCandles.Reset();
                    }
                    else
                    {
                        _averageSpreadOnTicks.Reset();
                        _topOnTicks.Reset();
                        _averageOnTicks.Reset();
                        _bottomOnTicks.Reset();
                    }
                }
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

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("Period Average Spread", 10, 5, 50, 1) {Comment = "Period SMA for spread values"}, //Для свечек ("Period Average Spread", 5, 2, 10, 1)
                new Parameter("Period Bollinger Bands", 20, 10, 100, 1) {Comment = "Period Bollinger Bands indicator"}, //Для свечек ("Period Bollinger Bands", 3, 2, 10, 1)
                new Parameter("Standard deviation", 2, 1, 4, 1) {Comment = "Standard deviation"},
                new Parameter("Offset for execution limit orders as market", 20) {Comment = "Example: Offset = Security.Tick * 20, 20 - value"},
                new Parameter("Stock order volume", 10) {Comment = "Example: GAZP@GAZP_Stocks - 10"},
                new Parameter("Futures order volume", 1) {Comment = "Example: GAZR@FORTS - 1"},
            };
        }

        public override List<BaseAnalyzerIndicator> AnalyzerIndicators() => HistoricalDataType == HistoricalDataType.Ticks ? TickIndicators() : CandlesIndicators();

        //Индикаторы для отрисовки на тиках
        private List<BaseAnalyzerIndicator> TickIndicators()
        {
            return new List<BaseAnalyzerIndicator>
            {
                new AnalyzerIndicator(new FuturesPriceOnTicks(), AnalyzerValue.Trade, 0)
                {
                    Name = "Futures Price",
                    Stroke = Colors.Violet,
                    Style = IndicatorStyle.Point,
                    Thickness = 2
                },
                new AnalyzerIndicator(new StockPriceOnTicks(), AnalyzerValue.Trade, 0)
                {
                    Name = "Stock Price",
                    Stroke = Colors.LightGreen,
                    Style = IndicatorStyle.Point,
                    Thickness = 2
                },
                new AnalyzerIndicator(new FuturesSpreadOnTicks(_volumeFutures), AnalyzerValue.Trade, 1)
                {
                    Name = "Futures Spread",
                    Stroke = Colors.Violet,
                    Style = IndicatorStyle.Line,
                    Thickness = 2
                },
                new AnalyzerIndicator(new StockSpreadOnTicks(_volumeStocks), AnalyzerValue.Trade, 1)
                {
                    Name = "Stock Spread",
                    Stroke = Colors.LightGreen,
                    Style = IndicatorStyle.Line,
                    Thickness = 2
                },
                new AnalyzerIndicator(new BollingerTopOnTicks((int) Parameter(2), (int) Parameter(3)), AnalyzerValue.Trade, 2)
                {
                    Name = "Bollinger Top",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Blue,
                    Thickness = 3
                },
                new AnalyzerIndicator(new BollingerAverageOnTicks((int) Parameter(2)), AnalyzerValue.Trade, 2)
                {
                    Name = "Bollinger Average",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Red,
                    Thickness = 3
                },
                new AnalyzerIndicator(new BollingerBottomOnTicks((int) Parameter(2), (int) Parameter(3)), AnalyzerValue.Trade, 2)
                {
                    Name = "Bollinger Bottom",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.CornflowerBlue,
                    Thickness = 3
                },
                new AnalyzerIndicator(new AverageSpreadOnTicks((int) Parameter(1)), AnalyzerValue.Trade, 2)
                {
                    Name = "Average Spread",
                    Stroke = Colors.Gray,
                    Style = IndicatorStyle.Line,
                    Thickness = 1
                }
            };
        }

        //Индикаторы для отрисовки на свечках
        private List<BaseAnalyzerIndicator> CandlesIndicators()
        {
            return new List<BaseAnalyzerIndicator>
            {
                new AnalyzerIndicator(new FuturesPriceOnCandles(), AnalyzerValue.Candle, 0)
                {
                    Name = "Futures Price",
                    Stroke = Colors.Violet,
                    Style = IndicatorStyle.Point,
                    Thickness = 2
                },
                new AnalyzerIndicator(new StockPriceOnCandles(), AnalyzerValue.Candle, 0)
                {
                    Name = "Stock Price",
                    Stroke = Colors.LightGreen,
                    Style = IndicatorStyle.Point,
                    Thickness = 2
                },
                new AnalyzerIndicator(new FuturesSpreadOnCandles(_volumeFutures), AnalyzerValue.Candle, 1)
                {
                    Name = "Futures Spread",
                    Stroke = Colors.Violet,
                    Style = IndicatorStyle.Line,
                    Thickness = 2
                },
                new AnalyzerIndicator(new StockSpreadOnCandles(_volumeStocks), AnalyzerValue.Candle, 1)
                {
                    Name = "Stock Spread",
                    Stroke = Colors.LightGreen,
                    Style = IndicatorStyle.Line,
                    Thickness = 2
                },
                new AnalyzerIndicator(new BollingerTopOnCandles((int) Parameter(2), (int) Parameter(3)), AnalyzerValue.Candle, 2)
                {
                    Name = "Bollinger Top",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Blue,
                    Thickness = 3
                },
                new AnalyzerIndicator(new BollingerAverageOnCandles((int) Parameter(2)), AnalyzerValue.Candle, 2)
                {
                    Name = "Bollinger Average",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.Red,
                    Thickness = 3
                },
                new AnalyzerIndicator(new BollingerBottomOnCandles((int) Parameter(2), (int) Parameter(3)), AnalyzerValue.Candle, 2)
                {
                    Name = "Bollinger Bottom",
                    Style = IndicatorStyle.Line,
                    Stroke = Colors.CornflowerBlue,
                    Thickness = 3
                },
                new AnalyzerIndicator(new AverageSpreadOnCandles((int) Parameter(1)), AnalyzerValue.Candle, 2)
                {
                    Name = "Average Spread",
                    Stroke = Colors.Gray,
                    Style = IndicatorStyle.Line,
                    Thickness = 1
                }
            };
        }

        //Логика торговой стратегии на тиках
        private void ProcessOnTicks(Trade tick)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var top = _topOnTicks.Add(tick);
                var bottom = _bottomOnTicks.Add(tick);
                var average = _averageOnTicks.Add(tick);
                var spread = _averageSpreadOnTicks.Add(tick);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_topOnTicks.IsFormed || !_bottomOnTicks.IsFormed || !_averageOnTicks.IsFormed || !_averageSpreadOnTicks.IsFormed) return;

                //Рассчитываем позицию
                Calculate(spread, top, average, bottom);
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessOnTicks");
            }
        }

        //Логика торговой стратегии на свечках
        private void ProcessOnCandles(Candle candle)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var top = _topOnCandles.Add(candle);
                var bottom = _bottomOnCandles.Add(candle);
                var average = _averageOnCandles.Add(candle);
                var spread = _averageSpreadOnCandles.Add(candle);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_topOnCandles.IsFormed || !_bottomOnCandles.IsFormed || !_averageOnCandles.IsFormed || !_averageSpreadOnCandles.IsFormed) return;

                //Рассчитываем позицию
                Calculate(spread, top, average, bottom);
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessOnCandles");
            }
        }

        //Рассчитываем позицию
        private void Calculate(decimal spread, decimal top, decimal average, decimal bottom)
        {
            //Выходим из позиции
            if ((!_waitFilled && _buySpread && spread >= average))
            {
                _buySpread = false;
                _sellSpread = false;

                RegisterOrders(false, $"Exit Position - spread {spread} > average {average}");
            }
            //Выходим из позиции
            else if ((!_waitFilled && _sellSpread && spread <= average))
            {
                _buySpread = false;
                _sellSpread = false;

                RegisterOrders(true, $"Exit Position - spread {spread} < average {average}");
            }
            //Покупаем спред
            else if (!_waitFilled && !_buySpread && spread <= bottom)
            {
                _buySpread = true;
                _sellSpread = false;

                RegisterOrders(true, $"Buy Spread - spread {spread} < bottom {bottom}");
            }
            //Продаем спред
            else if (!_waitFilled && !_sellSpread && spread >= top)
            {
                _buySpread = false;
                _sellSpread = true;

                RegisterOrders(false, $"Sell Spread - spread {spread} > top {top}");
            }
        }

        //Регистрируем заявки
        private void RegisterOrders(bool buySpread, string comment)
        {
            try
            {
                _waitFilled = true;
                _filledFutures = false;
                _filledStock = false;

                var directionFutures = buySpread ? Direction.Buy : Direction.Sell;
                var directionStock = buySpread ? Direction.Sell : Direction.Buy;

                var lastFutPrice = GetLastTick(_conName, _futures).Price;
                var lastStockPrice = GetLastTick(_conName, _stock).Price;

                if (HistoricalDataType == HistoricalDataType.Candles)
                {
                    lastFutPrice = GetLastCandle(_conName, _futures).ClosePrice;
                    lastStockPrice = GetLastCandle(_conName, _stock).ClosePrice;
                }

                var futPrice = buySpread ? lastFutPrice + _offsetFutures : lastFutPrice - _offsetFutures;
                var stockPrice = buySpread ? lastStockPrice - _offsetStock : lastStockPrice + _offsetStock;

                var futOrder = new Order(_futures)
                {
                    Type = OrderType.Limit,
                    Direction = directionFutures,
                    Volume = _volumeFutures,
                    Price = futPrice,
                    Comment = comment
                };

                var stockOrder = new Order(_stock)
                {
                    Type = OrderType.Limit,
                    Direction = directionStock,
                    Volume = _volumeStocks,
                    Price = stockPrice,
                    Comment = comment
                };

                futOrder.OrderChanged += order =>
                {
                    if (order.State == OrderState.Filled)
                        _filledFutures = true;

                    if (_filledFutures && _filledStock)
                        _waitFilled = false;
                };

                stockOrder.OrderChanged += order =>
                {
                    if (order.State == OrderState.Filled)
                        _filledStock = true;

                    if (_filledFutures && _filledStock)
                        _waitFilled = false;
                };

                RegisterOrder(futOrder);
                RegisterOrder(stockOrder);
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "ProcessOnCandles");
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
