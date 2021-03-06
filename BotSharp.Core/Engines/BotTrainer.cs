﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BotSharp.Core.Abstractions;
using BotSharp.Core.Agents;
using BotSharp.Core.Intents;
using DotNetToolkit;
using EntityFrameworkCore.BootKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BotSharp.Core.Engines
{
    public class BotTrainer
    {
        private Database dc;

        private string agentId;

        public BotTrainer()
        {
        }

        public BotTrainer(string agentId, Database dc)
        {
            this.dc = dc;
            this.agentId = agentId;
        }

        public async Task<ModelMetaData> Train(Agent agent, BotTrainOptions options)
        {
            var data = new NlpDoc();

            // Get NLP Provider
            var config = (IConfiguration)AppDomain.CurrentDomain.GetData("Configuration");
            var assemblies = (string[])AppDomain.CurrentDomain.GetData("Assemblies");
            var platform = config.GetSection($"BotPlatform").Value;
            string providerName = config.GetSection($"{platform}:Provider").Value;
            var provider = TypeHelper.GetInstance(providerName, assemblies) as INlpProvider;
            provider.Configuration = config.GetSection(platform);

            var pipeModel = new PipeModel
            {
                Name = providerName,
                Class = provider.ToString(),
                Meta = new JObject(),
                Time = DateTime.UtcNow
            };

            await provider.Load(agent, pipeModel);

            var settings = new PipeSettings
            {
                ProjectDir = options.AgentDir,
                AlgorithmDir = Path.Combine(AppDomain.CurrentDomain.GetData("ContentRootPath").ToString(), "Algorithms")
            };

            settings.ModelDir = Path.Combine(options.AgentDir, options.Model);

            if (!Directory.Exists(settings.ProjectDir))
            {
                Directory.CreateDirectory(settings.ProjectDir);
            }

            if (!Directory.Exists(settings.TempDir))
            {
                Directory.CreateDirectory(settings.TempDir);
            }

            if (!Directory.Exists(settings.ModelDir))
            {
                Directory.CreateDirectory(settings.ModelDir);
            }

            var meta = new ModelMetaData
            {
                Platform = platform,
                Language = agent.Language,
                TrainingDate = DateTime.UtcNow,
                Version = config.GetValue<String>($"Version"),
                Pipeline = new List<PipeModel>() { pipeModel },
                Model = settings.ModelDir
            };

            // pipe process
            var pipelines = provider.Configuration.GetValue<String>($"pipe")
                .Split(',')
                .Select(x => x.Trim())
                .ToList();

            for (int pipeIdx = 0; pipeIdx < pipelines.Count; pipeIdx++)
            {
                var pipe = TypeHelper.GetInstance(pipelines[pipeIdx], assemblies) as INlpTrain;
                // set configuration to current section
                pipe.Configuration = provider.Configuration.GetSection(pipelines[pipeIdx]);
                pipe.Settings = settings;
                pipeModel = new PipeModel
                {
                    Name = pipelines[pipeIdx],
                    Class = pipe.ToString(),
                    Time = DateTime.UtcNow
                };
                meta.Pipeline.Add(pipeModel);

                await pipe.Train(agent, data, pipeModel);
            }

            // save model meta data
            var metaJson = JsonConvert.SerializeObject(meta, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            File.WriteAllText(Path.Combine(settings.ModelDir, "model-meta.json"), metaJson);

            Console.WriteLine(metaJson);

            return meta;
        }
    }
}
