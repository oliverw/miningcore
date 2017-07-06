using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using MiningCore.Transport;

namespace MiningCore
{
    class EchoHandler
    {
        public EchoHandler(IConnection connection)
        {
            connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes("Ready.\n"));

            connection.Input
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Subscribe(x =>
                {
                    var msg = System.Text.Encoding.UTF8.GetString(x);

                    for (int i = 0; i < 20; i++)
                    {
                        var msg2 = $"{i} - You wrote: {msg}";
                        connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes(msg2));
                    }
                });
        }
    }
}
