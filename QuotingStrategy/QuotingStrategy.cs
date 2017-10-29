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

namespace QuotingStrategy
{
    // Описание:
    // Стратегия работает на пробое уровней комбинированным индикатором EMA-RSI.

    // Исходные данные: свечки или тики.
    // Свечки используются для расчета индикаторов.
    // Для более точного котирования в качестве исходных данных можно использовать тики.

    // Алгоритм:
    // Когда комбинированный индикатор EMA-RSI пробивает верхний уровень Max Level - продаем, нижний уровень Min Level - покупаем.

    // Для формирования и исполнения позиции используем котирование Quoting.

    // Индикаторы:
    // Индикатор RSI сглаженный экспоненциальной скользящей средней EMA.
    
    public class QuotingStrategy : IStrategy
    {
        private MultiIndicator<decimal> _emaRsi;
        private decimal _maxLevel;
        private decimal _minLevel;
        private int _volume;

        private Quoting _quoting;

        private CustomAnalyzerIndicator _quotingOrderChanged;

        private DateTime _lastTime;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Создаем комбинированный индикатор: RSI сглаженный EMA 
                _emaRsi = new MultiIndicator<decimal>(new RSI((int)Parameter(2)), new List<IIndicator<decimal>> {new EMA((int)Parameter(1))});

                //Уровни пробоя
                _maxLevel = Parameter(3);
                _minLevel = Parameter(4);

                //Объем заявки
                _volume = (int) Parameter(5);

                //Инициализируем котирование
                InitializationQuoting();

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
                    _emaRsi.Reset();
            };

            //Подписываемся на свечки
            NewCandle += candle =>
            {
                if (HistoricalDataType == HistoricalDataType.Candles)
                {
                    _lastTime = candle.CloseTime;
                    _quoting.Add(candle);
                }

                Process(candle.ClosePrice);
            };

            //Подписываемся на тики
            NewTick += tick =>
            {
                if (HistoricalDataType == HistoricalDataType.Ticks || HistoricalDataType == HistoricalDataType.None)
                {
                    _lastTime = tick.Time;
                    _quoting.Add(tick);
                }
            };

            if (StrategyMode == StrategyMode.Trading)
            {
                //Подписываемся на изменение биржевого стакана
                MarketDepthChanged += md => _quoting.Add(md);
            }
        }

        //Инициализируем котирование
        private void InitializationQuoting()
        {
            //Используем котирование, которое работает только на лимитных заявках
            _quoting = new Quoting(this, (int) Parameter(6))
            {
                MaxFrequencyMovingOrder = TimeSpan.FromSeconds((int)Parameter(7)),
                MaxQuotingTime = TimeSpan.FromSeconds((int)Parameter(8)),
                UseModifyOrder = false
            };

            //Выводим в лог события возникающие в процессе котирования
            _quoting.PositionChanged += position => MessageToLog($"Quoting: New position ({position})");
            _quoting.Stopped += () => MessageToLog("Quoting stopped");
            _quoting.MaxQuotingTimeExpired += MessageToLog;
            _quoting.Errors += MessageToLog;
            _quoting.Complete += (a1, a2) => MessageToLog("Quoting complete");

            _quoting.OrderChanged += (price, volume) =>
            {
                MessageToLog($"Quoting: Order changed ({price} | {volume})");

                //Визуализируем на графике место, где заявка была переставлена
                _quotingOrderChanged.Add(_lastTime, (double)(price + GetSecurity().Tick * 20), $"Quoting: Order changed ({price} | {volume})");
            };
        }

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("EMA period", 5) {Comment = "Period EMA indicator"},
                new Parameter("RSI period", 14) {Comment = "Period RSI indicator"},
                new Parameter("Max level, %", 70) {Comment = "Max level line"},
                new Parameter("Min level, %", 30) {Comment = "Min level line"},
                new Parameter("Volume", 5) {Comment = "Order volume"},
                new Parameter("Price offset", 10) {Comment = "Price offset in a market depth in the number of security tick"},
                new Parameter("Max frequency moving order", 10) {Comment = "Maximum frequency of moving order in seconds"},
                new Parameter("Max quoting time", 60) {Comment = "Maximum quoting time in seconds"}
            };
        }

        //Индикаторы для отрисовки в Analyzer
        public override List<BaseAnalyzerIndicator> AnalyzerIndicators()
        {
            var indicators = HistoricalDataType == HistoricalDataType.Ticks ? TickIndicators() : CandlesIndicators();

            //Кастомный индикатор для вывода на график точек перестановки ордеров в процессе котирования
            indicators.Add(_quotingOrderChanged = new CustomAnalyzerIndicator("Quoting", 0)
            {
                Stroke = Colors.Pink,
                Style = IndicatorStyle.Point,
                Thickness = 2,
                RadiusPoint = 15
            });

            return indicators;
        }

        //Индикаторы для отрисовки на тиках
        private List<BaseAnalyzerIndicator> TickIndicators()
        {
            return new List<BaseAnalyzerIndicator>
                {
                    new AnalyzerIndicator(new RSI((int) Parameter(2)), AnalyzerValue.TradePrice, 1)
                    {
                        Name = "RSI",
                        Stroke = Colors.Goldenrod,
                        Thickness = 2
                    },
                    new AnalyzerIndicator(new MultiIndicator<decimal>(new RSI((int)Parameter(2)), new List<IIndicator<decimal>> {new EMA((int)Parameter(1))}), AnalyzerValue.TradePrice, 1)
                    {
                        Name = "EMA-RSI",
                        Stroke = Colors.DarkViolet,
                        Thickness = 2
                    },
                    new AnalyzerIndicator(new HorizontalLine((int) Parameter(3)), AnalyzerValue.TradePrice, 1)
                    {
                        Name = "Max Level",
                        Stroke = Colors.Red,
                        Thickness = 1
                    },
                    new AnalyzerIndicator(new HorizontalLine(50), AnalyzerValue.TradePrice, 1)
                    {
                        Name = "Mid Level",
                        Stroke = Colors.Gray,
                        Thickness = 1
                    },
                    new AnalyzerIndicator(new HorizontalLine((int) Parameter(4)), AnalyzerValue.TradePrice, 1)
                    {
                        Name = "Min Level",
                        Stroke = Colors.LightGreen,
                        Thickness = 1
                    }
                };
        }

        //Индикаторы для отрисовки на свечках
        private List<BaseAnalyzerIndicator> CandlesIndicators()
        {
            return new List<BaseAnalyzerIndicator>
                {
                    new AnalyzerIndicator(new RSI((int) Parameter(2)), AnalyzerValue.CandleClosePrice, 1)
                    {
                        Name = "RSI",
                        Stroke = Colors.Goldenrod,
                        Thickness = 2
                    },
                    new AnalyzerIndicator(new MultiIndicator<decimal>(new RSI((int)Parameter(2)), new List<IIndicator<decimal>> {new EMA((int)Parameter(1))}), AnalyzerValue.CandleClosePrice, 1)
                    {
                        Name = "EMA-RSI",
                        Stroke = Colors.DarkViolet,
                        Thickness = 2
                    },
                    new AnalyzerIndicator(new HorizontalLine((int) Parameter(3)), AnalyzerValue.CandleClosePrice, 1)
                    {
                        Name = "Max Level",
                        Stroke = Colors.Red,
                        Thickness = 1
                    },
                    new AnalyzerIndicator(new HorizontalLine(50), AnalyzerValue.CandleClosePrice, 1)
                    {
                        Name = "Mid Level",
                        Stroke = Colors.Gray,
                        Thickness = 1
                    },
                    new AnalyzerIndicator(new HorizontalLine((int) Parameter(4)), AnalyzerValue.CandleClosePrice, 1)
                    {
                        Name = "Min Level",
                        Stroke = Colors.LightGreen,
                        Thickness = 1
                    }
                };
        }

        //Логика торговой стратегии
        private void Process(decimal price)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var emaRsi = _emaRsi.Add(price);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_emaRsi.IsFormed) return;

                //Если индикатор пробил уровень и позиция еще не исполнена, то начинаем котировать
                if (emaRsi >= _maxLevel)
                {
                    if (GetTradeInfo().Position != -_volume && !_quoting.InProcess)
                        _quoting.SetPosition(-_volume);
                }
                else if (emaRsi <= _minLevel)
                {
                    if (GetTradeInfo().Position != _volume && !_quoting.InProcess)
                        _quoting.SetPosition(_volume);
                }
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "Process");
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
