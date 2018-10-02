using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MiningCore.Util;
using NLog;
using ZeroMQ;
using ZeroMQ.Monitoring;

namespace MiningCore.Extensions
{
    public static class ZmqExtensions
    {
        private static readonly ConcurrentDictionary<string, (byte[] PubKey, byte[] SecretKey)> knownKeys =
            new ConcurrentDictionary<string, (byte[] PubKey, byte[] SecretKey)>();

        private static readonly Lazy<(byte[] PubKey, byte[] SecretKey)> ownKey = new Lazy<(byte[] PubKey, byte[] SecretKey)>(() =>
        {
            if (!ZContext.Has("curve"))
                throw new NotSupportedException("ZMQ library does not support curve");

            Z85.CurveKeypair(out var pubKey, out var secretKey);
            return (pubKey, secretKey);
        });

        const int PasswordIterations = 5000;
        private static readonly byte[] NoSalt = Enumerable.Repeat((byte)0, 32).ToArray();

        private static byte[] DeriveKey(string password, int length = 32)
        {
            using (var kbd = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password),
                NoSalt, PasswordIterations, HashAlgorithmName.SHA256))
            {
                var block = kbd.GetBytes(length);
                return block;
            }
        }

        private static long monitorSocketIndex = 0;

        public static IObservable<ZMonitorEventArgs> MonitorAsObservable(this ZSocket socket)
        {
            return Observable.Defer(() => Observable.Create<ZMonitorEventArgs>(obs=>
            {
                var url = $"inproc://monitor{Interlocked.Increment(ref monitorSocketIndex)}";
                var monitor = ZMonitor.Create(socket.Context, url);
                var cts = new CancellationTokenSource();

                void OnEvent(object sender, ZMonitorEventArgs e)
                {
                    obs.OnNext(e);
                }

                monitor.AllEvents += OnEvent;

                socket.Monitor(url);
                monitor.Start(cts);

                return Disposable.Create(() =>
                {
                    using(new CompositeDisposable(monitor, cts))
                    {
                        monitor.AllEvents -= OnEvent;
                        monitor.Stop();
                    }
                });
            }));
        }

        public static void LogMonitorEvent(ILogger logger, ZMonitorEventArgs e)
        {
            logger.Info(()=> $"[ZMQ] [{e.Event.Address}] {Enum.GetName(typeof(ZMonitorEvents), e.Event.Event)} [{e.Event.EventValue}]");
        }

        /// <summary>
        /// Sets up server-side socket to utilize ZeroMQ Curve Transport-Layer Security
        /// </summary>
        public static void SetupCurveTlsServer(this ZSocket socket, string keyPlain, ILogger logger)
        {
            keyPlain = keyPlain?.Trim();

            if (string.IsNullOrEmpty(keyPlain))
                return;

            if (!ZContext.Has("curve"))
                logger.ThrowLogPoolStartupException("Unable to initialize ZMQ Curve Transport-Layer-Security. Your ZMQ library was compiled without Curve support!");

            // Get server's public key
            byte[] keyBytes = null;
            byte[] serverPubKey = null;

            if (!knownKeys.TryGetValue(keyPlain, out var serverKeys))
            {
                keyBytes = DeriveKey(keyPlain, 32);

                // Derive server's public-key from shared secret
                Z85.CurvePublic(out serverPubKey, keyBytes.ToZ85Encoded());
                knownKeys[keyPlain] = (keyBytes, serverPubKey);
            }

            else
            {
                keyBytes = serverKeys.Item1;
                serverPubKey = serverKeys.Item2;
            }

            // set socket options
            socket.CurveServer = true;
            socket.CurveSecretKey = keyBytes;
            socket.CurvePublicKey = serverPubKey;
        }

        /// <summary>
        /// Sets up client-side socket to utilize ZeroMQ Curve Transport-Layer Security
        /// </summary>
        public static void SetupCurveTlsClient(this ZSocket socket, string keyPlain, ILogger logger)
        {
            keyPlain = keyPlain?.Trim();

            if (string.IsNullOrEmpty(keyPlain))
                return;

            if (!ZContext.Has("curve"))
                logger.ThrowLogPoolStartupException("Unable to initialize ZMQ Curve Transport-Layer-Security. Your ZMQ library was compiled without Curve support!");

            // Get server's public key
            byte[] serverPubKey = null;

            if (!knownKeys.TryGetValue(keyPlain, out var serverKeys))
            {
                var keyBytes = DeriveKey(keyPlain, 32);
Console.WriteLine(keyBytes.ToHexString());
                // Derive server's public-key from shared secret
                Z85.CurvePublic(out serverPubKey, keyBytes.ToZ85Encoded());
                knownKeys[keyPlain] = (keyBytes, serverPubKey);
            }

            else
                serverPubKey = serverKeys.PubKey;

            // set socket options
            socket.CurveServer = false;
            socket.CurveServerKey = serverPubKey;
            socket.CurveSecretKey = ownKey.Value.SecretKey;
            socket.CurvePublicKey = ownKey.Value.PubKey;
        }
    }
}
