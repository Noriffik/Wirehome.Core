﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wirehome.Core.Contracts;
using Wirehome.Core.System;

namespace Wirehome.Core.Diagnostics
{
    public class DiagnosticsService : IService
    {
        private readonly ConcurrentBag<OperationsPerSecondCounter> _operationsPerSecondCounters = new ConcurrentBag<OperationsPerSecondCounter>();
        private readonly SystemCancellationToken _systemCancellationToken;
        
        private readonly ILogger _logger;

        public DiagnosticsService(SystemCancellationToken systemCancellationToken, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _systemCancellationToken = systemCancellationToken ?? throw new ArgumentNullException(nameof(systemCancellationToken));
            
            _logger = loggerFactory.CreateLogger<DiagnosticsService>();
        }

        public void Start()
        {
            Task.Run(() => ResetOperationsPerSecondCountersAsync(_systemCancellationToken.Token), _systemCancellationToken.Token).ConfigureAwait(false);
        }

        public OperationsPerSecondCounter CreateOperationsPerSecondCounter(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            var operationsPerSecondCounter = new OperationsPerSecondCounter(uid);
            _operationsPerSecondCounters.Add(operationsPerSecondCounter);

            return operationsPerSecondCounter;
        }

        private async Task ResetOperationsPerSecondCountersAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                    foreach (var operationsPerSecondCounter in _operationsPerSecondCounters)
                    {
                        operationsPerSecondCounter.Reset();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error while resetting OperationsPerSecondCounters.");
                }
            }
        }
    }
}
