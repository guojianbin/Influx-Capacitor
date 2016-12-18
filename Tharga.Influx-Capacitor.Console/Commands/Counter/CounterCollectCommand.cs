﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tharga.InfluxCapacitor.Collector.Event;
using Tharga.InfluxCapacitor.Collector.Handlers;
using Tharga.InfluxCapacitor.Collector.Interface;
using Tharga.Toolkit.Console.Command.Base;

namespace Tharga.InfluxCapacitor.Console.Commands.Counter
{
    internal class CounterCollectCommand : ActionCommandBase
    {
        private readonly IConfigBusiness _configBusiness;
        private readonly ICounterBusiness _counterBusiness;
        private readonly IPublisherBusiness _publisherBusiness;
        private readonly ISendBusiness _sendBusiness;
        private readonly ITagLoader _tagLoader;

        public CounterCollectCommand(IConfigBusiness configBusiness, ICounterBusiness counterBusiness, IPublisherBusiness publisherBusiness, ISendBusiness sendBusiness, ITagLoader tagLoader)
            : base("Collect", "Collect counter data and send to the database.")
        {
            _configBusiness = configBusiness;
            _counterBusiness = counterBusiness;
            _sendBusiness = sendBusiness;
            _tagLoader = tagLoader;
            _publisherBusiness = publisherBusiness;
        }

        public override async Task<bool> InvokeAsync(string paramList)
        {
            var config = _configBusiness.LoadFiles(new string[] { });
            var counterGroups = _counterBusiness.GetPerformanceCounterGroups(config).ToArray();

            var index = 0;
            var counterGroup = QueryParam("Group", GetParam(paramList, index++), counterGroups.Select(x => new KeyValuePair<IPerformanceCounterGroup, string>(x, x.Name)));

            var counterGroupsToRead = counterGroup != null ? new[] { counterGroup } : counterGroups;

            var processor = new Processor(_configBusiness, _counterBusiness, _publisherBusiness, _sendBusiness, _tagLoader);
            processor.EngineActionEvent += EngineActionEvent;

            var count = 0;
            foreach (var cg in counterGroupsToRead)
            {
                count += await processor.CollectAssync(cg, config.Application.Metadata);
            }
            //OutputInformation("Totally {0} metrics points where collected and sent to the database.", count);

            return true;
        }

        private void EngineActionEvent(object sender, EngineActionEventArgs e)
        {
            OutputLine(e.Message, e.OutputLevel);
        }
    }
}