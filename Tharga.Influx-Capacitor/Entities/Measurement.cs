using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Tharga.InfluxCapacitor.Interface;

namespace Tharga.InfluxCapacitor.Entities
{
    internal class StopwatchHighPrecision
    {
        private readonly long _frequency;
        private bool _isRunning;
        private long _start;
        private long _segment;
        private long _end;
        private bool _segmentReadActive;
        private bool _isPaused;
        private long _pause;
        private long _closedPauseTime;

        [DllImport("Kernel32.dll")]
        private static extern void QueryPerformanceCounter(ref long ticks);

        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long lpFrequency);

        public StopwatchHighPrecision()
        {
            QueryPerformanceFrequency(out _frequency);
        }

        public void Start()
        {
            if (_isRunning) return;
            QueryPerformanceCounter(ref _start);
            _segment = _start;
            _isRunning = true;
            _segmentReadActive = true;
        }

        public long ElapsedTotal
        {
            get
            {
                if (_isRunning)
                {
                    QueryPerformanceCounter(ref _end);
                }
                return ((_end - _start) * 10000000 / _frequency) -  GetPauseTime();
            }
        }

        public long ElapsedSegment
        {
            get
            {
                if (!_segmentReadActive)
                    return 0;

                if (_isRunning)
                {
                    var last = _segment;
                    QueryPerformanceCounter(ref _segment);
                    return ((_segment - last) * 10000000 / _frequency) - GetPauseTime();
                }
                else
                {
                    var last = _segment;
                    _segment = _end;
                    return ((_segment - last) * 10000000 / _frequency) - GetPauseTime();
                }
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            QueryPerformanceCounter(ref _end);
            _isRunning = false;
        }

        public void Reset()
        {
            QueryPerformanceCounter(ref _start);
            _segment = _start;
            if (!_isRunning)
            {
                _end = _start;
            }
        }

        public void Pause()
        {
            if (_isPaused) return;
            _isPaused = true;
            QueryPerformanceCounter(ref _pause);
        }

        private long GetPauseTime()
        {
            if (!_isPaused) return _closedPauseTime;
            long local = 0;
            QueryPerformanceCounter(ref local);
            return ((local - _pause) * 10000000 / _frequency) + _closedPauseTime;
        }

        public void Resume()
        {
            if (!_isPaused) return;
            _isPaused = false;
            long local = 0;
            QueryPerformanceCounter(ref local);
            _closedPauseTime += (local - _pause) * 10000000 / _frequency;
        }
    }

    public class Measurement : IMeasurement
    {
        private readonly StopwatchHighPrecision _sw;
        private long _pauseTime;
        public Dictionary<string, object> Fields { get; }
        public Dictionary<string, object> Tags { get; }
        public Dictionary<string, TimeSpan> Checkpoints { get; }

        public Measurement()
        {
            _sw = new StopwatchHighPrecision();
            Fields = new Dictionary<string, object>();
            Tags = new Dictionary<string, object>();
            Checkpoints = new Dictionary<string, TimeSpan>();
        }

        internal StopwatchHighPrecision Stopwatch => _sw;

        public void AddTag(string key, object value)
        {
            if (Tags.ContainsKey(key))
                Tags.Remove(key);

            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(value?.ToString())) return;

            Tags.Add(key, value);
        }

        public void AddField(string key, object value)
        {
            if (Fields.ContainsKey(key))
                Fields.Remove(key);

            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(value?.ToString())) return;

            Fields.Add(key, value);
        }

        public void AddCheckpoint(string name)
        {
            if (Checkpoints.ContainsKey(name))
                Checkpoints.Remove(name);

            if (string.IsNullOrEmpty(name)) return;

            Checkpoints.Add(name, new TimeSpan(_sw.ElapsedTotal - _pauseTime));
        }

        public void Pause()
        {
            _sw.Pause();
        }

        public void Resume()
        {
            _sw.Resume();
        }

        public TimeSpan GetElapsed()
        {
            return new TimeSpan(_sw.ElapsedTotal - _pauseTime);
        }
    }
}