//using System;
//using Tharga.InfluxCapacitor.Entities;
//using Tharga.InfluxCapacitor.Interface;
//using Tharga.Toolkit.Console.Command.Base;

//namespace Tharga.InfluxCapacitor.Console
//{
//    public class ConsoleQueueEvents : IQueueEvents
//    {
//        private readonly IConsole _console;

//        public ConsoleQueueEvents(IConsole console)
//        {
//            _console = console;
//        }

//        public void DebugMessageEvent(string message)
//        {
//            _console.WriteLine(message, OutputLevel.Information);
//        }

//        public void ExceptionEvent(Exception exception)
//        {
//            _console.WriteLine(exception.Message, OutputLevel.Error);
//        }

//        public void SendEvent(ISendEventInfo sendCompleteEventArgs)
//        {
//            _console.WriteLine(sendCompleteEventArgs.Message, ToLevel(sendCompleteEventArgs.Level));
//        }

//        private OutputLevel ToLevel(SendEventInfo.OutputLevel level)
//        {
//            switch (level)
//            {
//                case SendEventInfo.OutputLevel.Error:
//                    return OutputLevel.Error;
//                case SendEventInfo.OutputLevel.Warning:
//                    return OutputLevel.Warning;
//                case SendEventInfo.OutputLevel.Information:
//                    return OutputLevel.Information;
//                default:
//                    return OutputLevel.Default;
//            }
//        }

//        public void QueueChangedEvent(IQueueChangeEventInfo queueChangeEventInfo)
//        {
//            _console.WriteLine(queueChangeEventInfo.Message, OutputLevel.Information);
//        }
//    }
//}