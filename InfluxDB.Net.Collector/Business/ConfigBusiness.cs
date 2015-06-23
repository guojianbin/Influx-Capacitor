﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using InfluxDB.Net.Collector.Entities;
using InfluxDB.Net.Collector.Interface;

namespace InfluxDB.Net.Collector.Business
{
    public class ConfigBusiness : IConfigBusiness
    {
        private readonly IFileLoaderAgent _fileLoaderAgent;

        public ConfigBusiness(IFileLoaderAgent fileLoaderAgent)
        {
            _fileLoaderAgent = fileLoaderAgent;
        }

        public IConfig LoadFile(string configurationFilename)
        {
            return LoadFiles(new[] { configurationFilename });
        }

        public IConfig LoadFiles(string[] configurationFilenames)
        {
            if (!configurationFilenames.Any())
                configurationFilenames = GetConfigFiles();

            IDatabaseConfig database = null;
            var groups = new List<ICounterGroup>();

            foreach (var configurationFilename in configurationFilenames)
            {
                var fileData = _fileLoaderAgent.ReadAllText(configurationFilename);

                var document = new XmlDocument();
                document.LoadXml(fileData);

                var db = GetDatabaseConfig(document);
                var grp = GetCounterGroups(document).ToList();

                if (db != null)
                {
                    if (database != null)
                    {
                        throw new InvalidOperationException("There are database configuration sections in more than one file.");
                    }
                    database = db;
                }
                groups.AddRange(grp);
            }

            //TODO: This is used by the service to automatically try to load the configuration if there is one.
            if (database == null)
            {
                database = OpenDatabaseConfig();
            }

            var config = new Config(database, groups);
            return config;
        }

        private string GetAppDataFolder()
        {
            var path = _fileLoaderAgent.GetApplicationFolderPath();
            if (!_fileLoaderAgent.DoesDirectoryExist(path))
            {
                _fileLoaderAgent.CreateDirectory(path);

                if (!_fileLoaderAgent.DoesDirectoryExist(path))
                    throw new InvalidOperationException(string.Format("Unable to create directory {0}.", path));

                TestWriteDeleteAccess(path);
            }

            return path;
        }

        private void TestWriteDeleteAccess(string path)
        {
            var sampleFileName = path + "\\test.txt";
            _fileLoaderAgent.WriteAllText(sampleFileName, "ABC");

            if (!_fileLoaderAgent.DoesFileExist(sampleFileName))
                throw new InvalidOperationException(string.Format("Unable to create testfile {0} in application folder.", sampleFileName));

            _fileLoaderAgent.DeleteFile(sampleFileName);

            if (_fileLoaderAgent.DoesFileExist(sampleFileName))
                throw new InvalidOperationException(string.Format("Unable to delete testfile {0} in application folder.", sampleFileName));
        }

        public IDatabaseConfig OpenDatabaseConfig()
        {
            var path = GetAppDataFolder();
            var databaseConfigFilePath = path + "\\database.xml";
            if (!_fileLoaderAgent.DoesFileExist(databaseConfigFilePath))
                return new DatabaseConfig(null, null, null, null);

            var config = LoadFile(databaseConfigFilePath);
            return config.Database;
        }

        public void SaveDatabaseUrl(string url)
        {
            var config = OpenDatabaseConfig();
            var newDbConfig = new DatabaseConfig(url, config.Username, config.Password, config.Name);
            SaveDatabaseConfigEx(newDbConfig);
        }

        public void SaveDatabaseConfig(string databaseName, string username, string password)
        {
            var config = OpenDatabaseConfig();
            var newDbConfig = new DatabaseConfig(config.Url, username, password, databaseName);
            SaveDatabaseConfigEx(newDbConfig);
        }

        private void SaveDatabaseConfigEx(DatabaseConfig newDbConfig)
        {
            var path = GetAppDataFolder();
            var databaseConfigFilePath = path + "\\database.xml";

            var xml = new XmlDocument();
            var xme = xml.CreateElement("InfluxDB.Net.Collector");
            xml.AppendChild(xme);
            var dme = xml.CreateElement("Database");
            xme.AppendChild(dme);

            var xmeUrl = xml.CreateElement("Url");
            xmeUrl.InnerText = newDbConfig.Url;
            dme.AppendChild(xmeUrl);

            var xmeUsername = xml.CreateElement("Username");
            xmeUsername.InnerText = newDbConfig.Username;
            dme.AppendChild(xmeUsername);

            var xmePassword = xml.CreateElement("Password");
            xmePassword.InnerText = newDbConfig.Password;
            dme.AppendChild(xmePassword);

            var xmeName = xml.CreateElement("Name");
            xmeName.InnerText = newDbConfig.Name;
            dme.AppendChild(xmeName);

            var xmlData = xml.OuterXml;

            _fileLoaderAgent.WriteAllText(databaseConfigFilePath, xmlData);
        }

        private IEnumerable<ICounterGroup> GetCounterGroups(XmlDocument document)
        {
            var counterGroups = document.GetElementsByTagName("CounterGroup");
            foreach (XmlElement counterGroup in counterGroups)
            {
                yield return GetCounterGroup(counterGroup);
            }
        }

        private ICounterGroup GetCounterGroup(XmlElement counterGroup)
        {
            var name = GetString(counterGroup, "Name");
            var secondsInterval = GetInt(counterGroup, "SecondsInterval");

            var counters = counterGroup.GetElementsByTagName("Counter");
            var cts = new List<ICounter>();
            foreach (XmlElement counter in counters)
            {
                cts.Add(GetCounter(counter));
            }
            return new CounterGroup(name, secondsInterval, cts);
        }

        private static string GetString(XmlElement element, string name)
        {
            var attr = element.Attributes.GetNamedItem(name);
            if (attr == null || string.IsNullOrEmpty(attr.Value))
                throw new InvalidOperationException(string.Format("No {0} attribute specified for the CounterGroup.", name));
            return attr.Value;
        }

        private static int GetInt(XmlElement element, string name)
        {
            var stringValue = GetString(element, name);
            int value;
            if (!int.TryParse(stringValue, out value))
                throw new InvalidOperationException(string.Format("Cannot parse attribute {0} value to integer.", name));
            return value;
        }

        private ICounter GetCounter(XmlElement counter)
        {
            string categoryName = null;
            string counterName = null;
            string instanceName = null;
            foreach (XmlElement item in counter.ChildNodes)
            {
                switch (item.Name)
                {
                    case "CategoryName":
                        categoryName = item.InnerText;
                        break;
                    case "CounterName":
                        counterName = item.InnerText;
                        break;
                    case "InstanceName":
                        instanceName = item.InnerText;
                        break;
                }
            }
            return new Counter(categoryName, counterName, instanceName);
        }

        private static DatabaseConfig GetDatabaseConfig(XmlDocument document)
        {
            var databases = document.GetElementsByTagName("Database");
            if (databases.Count == 0)
                return null;

            string url = null;
            string username = null;
            string password = null;
            string name = null;
            foreach (XmlElement item in databases[0].ChildNodes)
            {
                switch (item.Name)
                {
                    case "Url":
                        url = item.InnerText;
                        break;
                    case "Username":
                        username = item.InnerText;
                        break;
                    case "Password":
                        password = item.InnerText;
                        break;
                    case "Name":
                        name = item.InnerText;
                        break;
                }
            }
            var database = new DatabaseConfig(url, username, password, name);
            return database;
        }

        private string[] GetConfigFiles()
        {
            var configFile = ConfigurationManager.AppSettings["ConfigFile"];

            string[] configFiles;
            if (!string.IsNullOrEmpty(configFile))
            {
                configFiles = configFile.Split(';');
            }
            else
            {
                var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                configFiles = _fileLoaderAgent.GetFiles(currentDirectory, "*.xml");
            }

            return configFiles;
        }
    }
}