using System.Collections.Generic;

namespace Tharga.InfluxCapacitor.Collector.Interface
{
    public interface ICounterGroup
    {
        string Name { get; }
        int SecondsInterval { get; }
        int RefreshInstanceInterval { get; }
        IEnumerable<ICounter> Counters { get; }
        IEnumerable<ITag> Tags { get; }
    }
}