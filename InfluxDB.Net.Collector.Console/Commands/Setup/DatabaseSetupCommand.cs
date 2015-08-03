﻿using System.Threading.Tasks;
using InfluxDB.Net.Collector.Entities;
using InfluxDB.Net.Collector.Interface;

namespace InfluxDB.Net.Collector.Console.Commands.Setup
{
    internal class DatabaseSetupCommand : SetupCommandBase
    {
        public DatabaseSetupCommand(IInfluxDbAgentLoader influxDbAgentLoader, IConfigBusiness configBusiness)
            : base("Database", "Setup the database", influxDbAgentLoader, configBusiness)
        {
        }

        public async override Task<bool> InvokeAsync(string paramList)
        {
            var index = 0;

            var url = await GetServerUrlAsync(paramList, index++, null);
            if (string.IsNullOrEmpty(url))
                return false;

            var config = new DatabaseConfig(url, string.Empty, string.Empty, string.Empty);
            var logonInfo = await GetUsernameAsync(paramList, index++, config);
            if (logonInfo == null)
                return false;

            StartService();

            return true;
        }
    }
}