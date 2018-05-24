using EventHandler;
using MiningCore.Api.Responses;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using WebSocketManager;
using WebSocketManager.Common;

namespace MiningCoreSockets
{
    internal class PipelineHandler<T> : WebSocketHandler
    {
        SocketEventHandler _socket;
        public PipelineHandler(WebSocketConnectionManager webSocketConnectionManager, SocketEventHandler handler) : base(webSocketConnectionManager)
        {
            _socket = handler;

            _socket.Subscribe<Block>(async action => { await SendBlock(action); });
        }


        public override async Task OnConnected(WebSocket socket)
        {
            await base.OnConnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);

            var message = new Message()
            {
                MessageType = MessageType.Text,
                Data = $"{socketId} is now connected"
            };

            await SendMessageToAllAsync(message);
        }

        private async Task SendBlock(Block block)
        {

                await InvokeClientMethodToAllAsync("pipeline", block);
        }

        public override async Task OnDisconnected(WebSocket socket)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);

            await base.OnDisconnected(socket);

            var message = new Message()
            {
                MessageType = MessageType.Text,
                Data = $"{socketId} disconnected"
            };
            await SendMessageToAllAsync(message);
        }
    }

}