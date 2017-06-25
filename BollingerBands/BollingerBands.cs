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

namespace BollingerBands
{
    // Описание:
    // Простая стратегия на основе индикатора линии Боллинджера (Bollinger Bands).

    // Исходные данные: свечки.

    // Алгоритм:
    // Свечка пересекает канал Боллинджера снизу вверх - продаем, сверху вниз - покупаем.

    // Индикаторы:
    // Линии Боллинджера - BollingerBandsTop, BollingerBandsBottom

    // Опции: 
    // - Добавлен вывод на график точек входа/выхода со смещением для визуального анализа

    public class BollingerBands : IStrategy
    {
        private BollingerBandsTop _top;
        private BollingerBandsBottom _bottom;
        private int _volume;

        private CustomAnalyzerIndicator _customValuesBuy;
        private CustomAnalyzerIndicator _customValuesSell;

        //Инициализация стратегии
        public override void Initialization()
        {
            try
            {
                //Создаем индикаторы для торговой стратегии
                _top = new BollingerBandsTop((int)Parameter(1), (int)Parameter(2));
                _bottom = new BollingerBandsBottom((int) Parameter(1), (int) Parameter(2));
              
                //Объем заявки
                _volume = (int)Parameter(3);

                //При остановке старегии сбрасываем индикаторы
                StrategyStateChanged += state =>
                {
                    if (state == StrategyState.NotActivated)
                    {
                        _top.Reset();
                        _bottom.Reset();
                    }
                };

                //Подписываемся на свечки
                NewCandle += ProcessCandle;
            }
            catch (Exception ex)
            {
                ExceptionMessage(ex, "Strategy");
            }
        }

        //Устанавливаем параметры для стратегии
        public override List<Parameter> StrategyParameters()
        {
            return new List<Parameter>
            {
                new Parameter("Period", 20, 10, 50, 1) {Comment = "Period Bollinger Bands indicator"},
                new Parameter("Standard deviation", 2, 1, 4, 1) {Comment = "Standard deviation"},
                new Parameter("Volume", 1) {Comment = "Order volume"}
            };
        }

        //Индикаторы для отрисовки в Analyzer
        public override List<BaseAnalyzerIndicator> AnalyzerIndicators()
        {
            var indicators = new List<BaseAnalyzerIndicator>
            {
                new AnalyzerIndicator(new BollingerBandsTop((int) Parameter(1), (int) Parameter(2)), AnalyzerValue.CandleClosePrice, 0)
                {
                    Name = "Top",
                    Stroke = Colors.Blue,
                    Thickness = 2
                },

                new AnalyzerIndicator(new BollingerBandsAverage((int) Parameter(1)), AnalyzerValue.CandleClosePrice, 0)
                {
                    Name = "Average",
                    Stroke = Colors.Red,
                    Thickness = 2
                },

                new AnalyzerIndicator(new BollingerBandsBottom((int) Parameter(1), (int) Parameter(2)), AnalyzerValue.CandleClosePrice, 0)
                {
                    Name = "Bottom",
                    Stroke = Colors.Blue,
                    Thickness = 2
                }
            };

            indicators.Add(_customValuesBuy = new CustomAnalyzerIndicator("BUY ORDER", 0)
            {
                Stroke = Colors.PaleGreen,
                Style = IndicatorStyle.Point,
                Thickness = 2,
                RadiusPoint = 15
            });

            indicators.Add(_customValuesSell = new CustomAnalyzerIndicator("SELL ORDER", 0)
            {
                Stroke = Colors.OrangeRed,
                Style = IndicatorStyle.Point,
                Thickness = 2,
                RadiusPoint = 15
            });

            return indicators;
        }

        //Логика торговой стратегии
        private void ProcessCandle(Candle candle)
        {
            try
            {
                //Добавляем новую свечку для расчетов в индикаторы
                //Добавляем до проверки актуальности данных, так как возможна предзагрузка исторических данных
                var top = _top.Add(candle.ClosePrice);
                var bottom = _bottom.Add(candle.ClosePrice);

                //Проверка актуальности данных
                if (!GetRealTimeData()) return;

                //Если индикаторы не сформированы, то ничего не делаем
                if (!_top.IsFormed || !_bottom.IsFormed) return;

                //Текущая позиция по выбранному коннектору и инструменту
                var position = GetTradeInfo().Position;

                if (candle.ClosePrice <= bottom && position <= 0)
                {
                    var limitOrder = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Buy,
                        Volume = position < 0 ? -position : _volume, //Закрываем полностью позицию или открываем новую
                        Price = candle.ClosePrice
                    };

                    _customValuesBuy.Add(candle.CloseTime, (double)(limitOrder.Price + GetSecurity().Tick * 25), $"BUY ORDER\nTime: {candle.CloseTime.ToString("dd.MM.yyyy HH:mm:ss.fff")}\nVolume: {limitOrder.Volume}");

                    //Отправляем лимитную заявку на регистрацию
                    RegisterOrder(limitOrder);
                }

                if (candle.ClosePrice >= top && position >= 0)
                {
                    var limitOrder = new Order
                    {
                        Type = OrderType.Limit,
                        Direction = Direction.Sell,
                        Volume = position < 0 ? -position : _volume,
                        Price = candle.ClosePrice
                    };

                    _customValuesSell.Add(candle.CloseTime, (double) (limitOrder.Price - GetSecurity().Tick*25), $"SELL ORDER\nTime: {candle.CloseTime.ToString("dd.MM.yyyy HH:mm:ss.fff")}\nVolume: {limitOrder.Volume}");

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
