// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Configuration;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Http;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.AspNet.SignalR.Json;
using Microsoft.AspNet.SignalR.WebSockets;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNet.SignalR.Transports
{
    public class WebSocketTransport : ForeverTransport
    {
        private readonly HostContext _context;
        private IWebSocket _socket;
        private bool _isAlive = true;

        private readonly int? _maxIncomingMessageSize;

        private readonly Action<string> _message;
        private readonly Action _closed;
        private readonly Action<Exception> _error;

        public WebSocketTransport(HostContext context,
                                  IServiceProvider serviceProvider)
            : this(context,
                   serviceProvider.GetService<JsonSerializer>(),
                   serviceProvider.GetService<ITransportHeartbeat>(),
                   serviceProvider.GetService<IPerformanceCounterManager>(),
                   serviceProvider.GetService<ILoggerFactory>(),
                   serviceProvider.GetService<IConfigurationManager>().MaxIncomingWebSocketMessageSize)
        {
        }

        public WebSocketTransport(HostContext context,
                                  JsonSerializer serializer,
                                  ITransportHeartbeat heartbeat,
                                  IPerformanceCounterManager performanceCounterWriter,
                                  ILoggerFactory loggerFactory,
                                  int? maxIncomingMessageSize)
            : base(context, serializer, heartbeat, performanceCounterWriter, loggerFactory)
        {
            _context = context;
            _maxIncomingMessageSize = maxIncomingMessageSize;

            _message = OnMessage;
            _closed = OnClosed;
            _error = OnSocketError;
        }

        public override bool IsAlive
        {
            get
            {
                return _isAlive;
            }
        }

        public override CancellationToken CancellationToken
        {
            get
            {
                return CancellationToken.None;
            }
        }

        public override Task KeepAlive()
        {
            // Ensure delegate continues to use the C# Compiler static delegate caching optimization.
            return EnqueueOperation(state =>
            {
                var webSocket = (IWebSocket)state;
                return webSocket.Send("{}");
            },
            _socket);
        }

        public override Task ProcessRequest(ITransportConnection connection)
        {
            if (IsAbortRequest)
            {
                return connection.Abort(ConnectionId);
            }
            else
            {
                return AcceptWebSocketRequest(socket =>
                {
                    _socket = socket;
                    socket.OnClose = _closed;
                    socket.OnMessage = _message;
                    socket.OnError = _error;

                    return ProcessRequestCore(connection);
                });
            }
        }

        protected override TextWriter CreateResponseWriter()
        {
            return new BinaryTextWriter(_socket);
        }

        public override Task Send(object value)
        {
            var context = new WebSocketTransportContext(this, value);

            // Ensure delegate continues to use the C# Compiler static delegate caching optimization.
            return EnqueueOperation(state => PerformSend(state), context);
        }

        public override Task Send(PersistentResponse response)
        {
            OnSendingResponse(response);

            return Send((object)response);
        }

        private Task AcceptWebSocketRequest(Func<IWebSocket, Task> callback)
        {
            // TODO: Websockets Accept
            //var accept = _context.Environment.Get<Action<IDictionary<string, object>, WebSocketFunc>>(OwinConstants.WebSocketAccept);

            //if (accept == null)
            //{
            //    // Bad Request
            //    _context.Response.StatusCode = 400;
            //    return _context.Response.End(Resources.Error_NotWebSocketRequest);
            //}

            //var handler = new OwinWebSocketHandler(callback, _maxIncomingMessageSize);
            //accept(null, handler.ProcessRequest);
            //return TaskAsyncHelper.Empty;
            return TaskAsyncHelper.Empty;
        }

        private static async Task PerformSend(object state)
        {
            var context = (WebSocketTransportContext)state;

            try
            {
                context.Transport.JsonSerializer.Serialize(context.State, context.Transport.OutputWriter);
                context.Transport.OutputWriter.Flush();

                await context.Transport._socket.Flush();
            }
            catch (Exception ex)
            {
                // OnError will close the socket in the event of a JSON serialization or flush error.
                // The client should then immediately reconnect instead of simply missing keep-alives.
                context.Transport.OnError(ex);
                throw;
            }
        }

        private void OnMessage(string message)
        {
            if (Received != null)
            {
                Received(message).Catch();
            }
        }

        private void OnClosed()
        {
            Logger.WriteInformation(String.Format("CloseSocket({0})", ConnectionId));

            // Require a request to /abort to stop tracking the connection. #2195
            _isAlive = false;
        }

        private void OnSocketError(Exception error)
        {
            Logger.WriteError(String.Format("OnError({0}, {1})", ConnectionId, error));
        }

        private class WebSocketTransportContext
        {
            public WebSocketTransport Transport;
            public object State;

            public WebSocketTransportContext(WebSocketTransport transport, object state)
            {
                Transport = transport;
                State = state;
            }
        }
    }
}
