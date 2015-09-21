﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using InfluxDB.Net;
using InfluxDB.Net.Models;
using Tharga.InfluxCapacitor.Collector.Event;
using Tharga.InfluxCapacitor.Collector.Interface;
using Tharga.Toolkit.Console.Command.Base;

namespace Tharga.InfluxCapacitor.Collector.Handlers
{
    internal class CollectorEngine
    {
        public event EventHandler<CollectRegisterCounterValuesEventArgs> CollectRegisterCounterValuesEvent;

        private readonly IPerformanceCounterGroup _performanceCounterGroup;
        private readonly ISendBusiness _sendBusiness;
        private readonly Timer _timer;
        private readonly string _name;
        private readonly ITag[] _tags;
        private StopwatchHighPrecision _sw;
        private DateTime? _timestamp;
        private long _counter;
        private int _missCounter;
        private int _refreshCountdown = 0;

        public CollectorEngine(IPerformanceCounterGroup performanceCounterGroup, ISendBusiness sendBusiness, ITagLoader tagLoader)
        {
            _performanceCounterGroup = performanceCounterGroup;
            _sendBusiness = sendBusiness;
            _tags = tagLoader.GetGlobalTags().Union(_performanceCounterGroup.Tags).ToArray();
            if (performanceCounterGroup.SecondsInterval > 0)
            {
                _timer = new Timer(1000 * performanceCounterGroup.SecondsInterval);
                _timer.Elapsed += Elapsed;
            }
            _name = _performanceCounterGroup.Name;            
        }

        private async void Elapsed(object sender, ElapsedEventArgs e)
        {
            await CollectRegisterCounterValuesAsync();
        }

        private static DateTime Floor(DateTime dateTime, TimeSpan interval)
        {
            return dateTime.AddTicks(-(dateTime.Ticks % interval.Ticks));
        }

        public async Task<int> CollectRegisterCounterValuesAsync()
        {
            try
            {
                var swMain = new StopwatchHighPrecision();
                var timeInfo = new Dictionary<string, long>();

                double elapseOffset = 0;
                if (_timestamp == null)
                {
                    _sw = new StopwatchHighPrecision();
                    _timestamp = DateTime.UtcNow;
                    var nw = Floor(_timestamp.Value, new TimeSpan(0, 0, 0, 1));
                    _timestamp = nw;
                }
                else
                {
                    var elapsedTotal = _sw.ElapsedTotal;

                    elapseOffset = new TimeSpan(elapsedTotal).TotalSeconds - _performanceCounterGroup.SecondsInterval * _counter;

                    if (_missCounter >= 6)
                    {
                        //Reset everything and start over.
                        OnCollectRegisterCounterValuesEvent(new CollectRegisterCounterValuesEventArgs(_name, string.Format("Missed {0} cycles. Resetting and starting over.", _missCounter), OutputLevel.Warning));

                        _timestamp = null;
                        _counter = 0;
                        _missCounter = 0;
                        return -4;
                    }

                    if (elapseOffset > _performanceCounterGroup.SecondsInterval)
                    {
                        _missCounter++;
                        _counter = _counter + 1 + (int)(elapseOffset / _performanceCounterGroup.SecondsInterval);
                        OnCollectRegisterCounterValuesEvent(new CollectRegisterCounterValuesEventArgs(_name, string.Format("Dropping {0} steps.", (int)elapseOffset), OutputLevel.Warning));
                        return -2;
                    }

                    if (elapseOffset < _performanceCounterGroup.SecondsInterval * -1)
                    {
                        _missCounter++;
                        OnCollectRegisterCounterValuesEvent(new CollectRegisterCounterValuesEventArgs(_name, string.Format("Jumping 1 step. ({0} = new TimeSpan({1}).TotalSeconds - {2} * {3})", (int)elapseOffset, elapsedTotal, _performanceCounterGroup.SecondsInterval, _counter), OutputLevel.Warning));
                        return -3;
                    }

                    _missCounter = 0;

                    //Adjust interval
                    var next = 1000 * (_performanceCounterGroup.SecondsInterval - elapseOffset);
                    if (next > 0)
                    {
                        _timer.Interval = next;
                    }
                }

                var timestamp = _timestamp.Value.AddSeconds(_performanceCounterGroup.SecondsInterval * _counter);
                _counter++;

                var precision = TimeUnit.Seconds;
                timeInfo.Add("Synchronize", swMain.ElapsedSegment);

                //TODO: Create a mutex lock here (So that two counters canno read the same signature at the same time, since the content of the _performanceCounterGroup might change during this process.

                //Prepare read
                var performanceCounterInfos = PrepareCounters();
                timeInfo.Add("Prepare", swMain.ElapsedSegment);

                //Perform Read (This should be as fast and short as possible)
                var values = ReadValues(performanceCounterInfos);
                timeInfo.Add("Read", swMain.ElapsedSegment);

                //Prepare result                
                var points = FormatResult(performanceCounterInfos, values, precision, timestamp).ToArray();
                timeInfo.Add("Format", swMain.ElapsedSegment);

                //Queue result
                _sendBusiness.Enqueue(points);
                timeInfo.Add("Enque", swMain.ElapsedSegment);

                //Cleanup
                RemoveObsoleteCounters(values, performanceCounterInfos);
                timeInfo.Add("Cleanup", swMain.ElapsedSegment);

                OnCollectRegisterCounterValuesEvent(new CollectRegisterCounterValuesEventArgs(_name, points.Count(), timeInfo, elapseOffset, OutputLevel.Default));

                //TODO: Release mutex

                return points.Length;
            }
            catch (Exception exception)
            {
                OnCollectRegisterCounterValuesEvent(new CollectRegisterCounterValuesEventArgs(_name, exception));
                return -1;
            }
        }

        private IPerformanceCounterInfo[] PrepareCounters()
        {
            if (_refreshCountdown == 0)
            {
                _refreshCountdown = _performanceCounterGroup.RefreshInstanceInterval - 1;
                return _performanceCounterGroup.GetFreshCounters().ToArray();
            }

            _refreshCountdown--;

            if (_refreshCountdown < 0)
                _refreshCountdown = _performanceCounterGroup.RefreshInstanceInterval - 1;

            return _performanceCounterGroup.GetCounters().ToArray();
        }

        private void RemoveObsoleteCounters(float?[] values, IPerformanceCounterInfo[] performanceCounterInfos)
        {
            if (!values.Any(x => !x.HasValue))
                return;

            for (var i = 0; i < values.Count(); i++)
            {
                _performanceCounterGroup.RemoveCounter(performanceCounterInfos[i]);
            }

            Trace.TraceInformation("Removed {0} counters.", values.Count());
        }

        private static float?[] ReadValues(IPerformanceCounterInfo[] performanceCounterInfos)
        {
            var values = new float?[performanceCounterInfos.Length];
            for (var i = 0; i < values.Count(); i++)
            {
                try
                {
                    values[i] = performanceCounterInfos[i].PerformanceCounter.NextValue();
                }
                catch (InvalidOperationException)
                {
                    values[i] = null;
                }
            }
            return values;
        }

        private IEnumerable<Point> FormatResult(IPerformanceCounterInfo[] performanceCounterInfos, float?[] values, TimeUnit precision, DateTime timestamp)
        {
            for (var i = 0; i < values.Count(); i++)
            {
                var value = values[i];
                if (value != null)
                {
                    var performanceCounterInfo = performanceCounterInfos[i];

                    var categoryName = performanceCounterInfo.PerformanceCounter.CategoryName;
                    var counterName = performanceCounterInfo.PerformanceCounter.CounterName;
                    var key = performanceCounterInfo.PerformanceCounter.InstanceName;
                    var tags = GetTags(_tags, categoryName, counterName);
                    var fields = new Dictionary<string, object>
                                     {
                                         { "value", value },
                                         //{ "readSpan", readSpan }, //Time in ms from the first, to the lats counter read in the group.
                                         //{ "timeOffset", (float)(timeOffset * 1000) } //Time difference in ms from reported time, to when read actually started.
                                     };

                    var point = new Point
                                    {
                                        Name = _name,
                                        Tags = tags,
                                        Fields = fields,
                                        Precision = precision,
                                        Timestamp = timestamp
                                    };

                    if (!string.IsNullOrEmpty(key))
                    {
                        point.Tags.Add("instance", key);
                    }

                    yield return point;
                }
            }
        }

        private static Dictionary<string, string> GetTags(IEnumerable<ITag> globalTags, string categoryName, string counterName)
        {
            var dictionary = new Dictionary<string, string>
            {
                { "hostname", Environment.MachineName },
                { "category", categoryName },
                { "counter", counterName },
            };
            foreach (var tag in globalTags)
            {
                dictionary.Add(tag.Name, tag.Value);
            }
            return dictionary;
        }

        public async Task StartAsync()
        {
            if (_timer == null) return;
            await CollectRegisterCounterValuesAsync();
            _timer.Start();
        }

        protected virtual void OnCollectRegisterCounterValuesEvent(CollectRegisterCounterValuesEventArgs e)
        {
            var handler = CollectRegisterCounterValuesEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}