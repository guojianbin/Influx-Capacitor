using System.Collections.Generic;
using InfluxDB.Net.Models;

namespace Tharga.InfluxCapacitor.Interface
{
    public interface IQueue
    {
        void Enqueue(Point point);
        void Enqueue(Point[] points);
        IQueueCountInfo GetQueueInfo();
        IEnumerable<Point> Items { get; }
    }
}