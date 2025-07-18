/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Logging;
using QuantConnect.Optimizer.gRPC;
using QuantConnect.Optimizer.Parameters;
using QuantConnect.Util;

namespace QuantConnect.Optimizer
{
    public class DistributedOptimizationOptimizer : LeanOptimizer
    {
        private readonly string _optimizationId;
        private readonly string _resultsDestinationFolder;
        private readonly ConcurrentDictionary<string, OptimizerWorker.OptimizerWorkerClient> _clients = new();
        private readonly ConcurrentDictionary<string, ChannelBase> _channels = new();
        private readonly string _nodeIdPrefix = "optimizer-node-";
        private int _nodeIdCounter;

        public DistributedOptimizationOptimizer(OptimizationNodePacket nodePacket) : base(nodePacket)
        {
            _optimizationId = nodePacket.OptimizationId;
            _resultsDestinationFolder = Path.Combine(Config.Get("results-destination-folder", "/Results"), _optimizationId);
            Directory.CreateDirectory(_resultsDestinationFolder);

            var workerAddresses = Config.Get("optimizer-nodes", "").Split(',');
            if (workerAddresses.Length == 0 || string.IsNullOrEmpty(workerAddresses[0]))
            {
                throw new ArgumentException("No worker nodes specified. Please set the 'optimizer-nodes' configuration value.");
            }

            foreach (var address in workerAddresses)
            {
                var channel = GrpcChannel.ForAddress(address);
                var client = new OptimizerWorker.OptimizerWorkerClient(channel);
                var nodeId = $"{_nodeIdPrefix}{Interlocked.Increment(ref _nodeIdCounter)}";
                _clients[nodeId] = client;
                _channels[nodeId] = channel;
            }
        }

        protected override string RunLean(ParameterSet parameterSet, string backtestName)
        {
            var backtestId = Guid.NewGuid().ToString();
            var optimizationPacketJson = JsonConvert.SerializeObject(NodePacket);

            var request = new BacktestRequest
            {
                OptimizationId = _optimizationId,
                BacktestId = backtestId,
                OptimizationPacket = optimizationPacketJson
            };

            foreach (var parameter in parameterSet.Value)
            {
                request.Parameters.Add(new gRPC.Parameter { Name = parameter.Key, Value = parameter.Value });
            }

            Task.Run(async () =>
            {
                var client = GetNextAvailableClient();
                try
                {
                    using var call = client.RunBacktest(request);
                    await foreach (var result in call.ResponseStream.ReadAllAsync())
                    {
                        if (!string.IsNullOrEmpty(result.JsonResult))
                        {
                            NewResult(result.JsonResult, result.BacktestId);
                        }
                        else if (result.FileChunk.Length > 0)
                        {
                            var filePath = Path.Combine(_resultsDestinationFolder, result.BacktestId, result.FileName);
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                            await File.AppendAllBytesAsync(filePath, result.FileChunk.ToByteArray());
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Error running backtest {backtestId} on client.");
                    NewResult(null, backtestId);
                }
            });

            return backtestId;
        }

        protected override void AbortLean(string backtestId)
        {
            // In a real implementation, we would need to send a cancellation request to the worker.
            // For this example, we'll just log it.
            Log.Trace($"AbortLean not implemented for distributed optimizer. BacktestId: {backtestId}");
        }

        protected override void SendUpdate()
        {
            // This can be implemented to send updates to a central monitoring service if needed.
        }

        public override void Dispose()
        {
            foreach (var channel in _channels.Values)
            {
                (channel as GrpcChannel)?.Dispose();
            }
            base.Dispose();
        }

        private OptimizerWorker.OptimizerWorkerClient GetNextAvailableClient()
        {
            // Simple round-robin for this example. A more sophisticated load balancer could be used.
            var nodeIds = _clients.Keys.ToList();
            var nodeId = nodeIds[CompletedBacktests % nodeIds.Count];
            return _clients[nodeId];
        }
    }
}
