// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using NLog;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    internal class LibuvTrace : ILibuvTrace
    {
	    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

		// ConnectionRead: Reserved: 3

		private static readonly Action<ILogger, string, Exception> _connectionPause = (l, ConnectionId, ex)=>
            l.Debug(()=> $@"Connection id ""{ConnectionId}"" paused.");

        private static readonly Action<ILogger, string, Exception> _connectionResume = (l, ConnectionId, ex) =>
	        l.Debug(() => $@"Connection id ""{ConnectionId}"" resumed.");

        private static readonly Action<ILogger, string, Exception> _connectionReadFin = (l, ConnectionId, ex) =>
	        l.Debug(() => $@"Connection id ""{ConnectionId}"" received FIN.");

        private static readonly Action<ILogger, string, Exception> _connectionWriteFin = (l, ConnectionId, ex) =>
	        l.Debug(() => $@"Connection id ""{ConnectionId}"" sending FIN.");

        private static readonly Action<ILogger, string, int, Exception> _connectionWroteFin = (l, ConnectionId, Status, ex) =>
	        l.Debug(() => $@"Connection id ""{ConnectionId}"" sent FIN with status ""{Status}"".");

        // ConnectionWrite: Reserved: 11

        // ConnectionWriteCallback: Reserved: 12

        private static readonly Action<ILogger, string, Exception> _connectionError = (l, ConnectionId, ex) =>
	        l.Info(() => $@"Connection id ""{ConnectionId}"" communication error.");

        private static readonly Action<ILogger, string, Exception> _connectionReset = (l, ConnectionId, ex) =>
	        l.Debug(() => $@"Connection id ""{ConnectionId}"" reset.");

        public void ConnectionRead(string connectionId, int count)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 3
        }

        public void ConnectionReadFin(string connectionId)
        {
            _connectionReadFin(_logger, connectionId, null);
        }

        public void ConnectionWriteFin(string connectionId)
        {
            _connectionWriteFin(_logger, connectionId, null);
        }

        public void ConnectionWroteFin(string connectionId, int status)
        {
            _connectionWroteFin(_logger, connectionId, status, null);
        }

        public void ConnectionWrite(string connectionId, int count)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 11
        }

        public void ConnectionWriteCallback(string connectionId, int status)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 12
        }

        public void ConnectionError(string connectionId, Exception ex)
        {
            _connectionError(_logger, connectionId, ex);
        }

        public void ConnectionReset(string connectionId)
        {
            _connectionReset(_logger, connectionId, null);
        }

        public void ConnectionPause(string connectionId)
        {
            _connectionPause(_logger, connectionId, null);
        }

        public void ConnectionResume(string connectionId)
        {
            _connectionResume(_logger, connectionId, null);
        }

	    public void LogError(int _, Exception exception, string msg)
	    {
		    _logger.Error(exception, msg);
	    }

        public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, object _, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => _logger.Log(logLevel, formatter(state, exception));
    }
}
