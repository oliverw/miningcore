using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Stratum;
using NLog;

namespace MiningCore.Tests.Stratum
{
	public class TestStratumServer : StratumServer<WorkerContextBase>,
		IDisposable
	{
		public TestStratumServer() : base(ModuleInitializer.Container.Resolve<IComponentContext>())
		{
			Connects = connectSubject.AsObservable();
			Disconnects = disconnectSubject.AsObservable();
			ReceiveCompleted = receiveCompletedSubject.AsObservable();
			ReceiveError = receiveErrorSubject.AsObservable();
			Requests = requestSubject.AsObservable();

			LogCat = nameof(TestStratumServer);
			logger = LogManager.CreateNullLogger();
		}

		readonly ReplaySubject<StratumClient<WorkerContextBase>> connectSubject = new ReplaySubject<StratumClient<WorkerContextBase>>(1);
		readonly ReplaySubject<Unit> disconnectSubject = new ReplaySubject<Unit>(1);
		readonly ReplaySubject<StratumClient<WorkerContextBase>> receiveCompletedSubject = new ReplaySubject<StratumClient<WorkerContextBase>>(1);
		readonly ReplaySubject<StratumClient<WorkerContextBase>> receiveErrorSubject = new ReplaySubject<StratumClient<WorkerContextBase>>(1);
		readonly ReplaySubject<(StratumClient<WorkerContextBase> Client, Timestamped<JsonRpcRequest> TsRequest)> requestSubject =
			new ReplaySubject<(StratumClient<WorkerContextBase> Client, Timestamped<JsonRpcRequest> TsRequest)>();

		private int connectCount = 0;
		private int disconnectCount = 0;
		private int receiveCompletedCount = 0;
		private int receiveErrorCount = 0;

		public IObservable<StratumClient<WorkerContextBase>> Connects { get; }
		public IObservable<Unit> Disconnects { get; }
		public IObservable<StratumClient<WorkerContextBase>> ReceiveCompleted { get; }
		public IObservable<StratumClient<WorkerContextBase>> ReceiveError { get; }
		public IObservable<(StratumClient<WorkerContextBase> Client, Timestamped<JsonRpcRequest> TsRequest)> Requests { get; }

		public int ConnectCount => connectCount;
		public int DisconnectCount => disconnectCount;
		public int ReceiveCompletedCount => receiveCompletedCount;
		public int ReceiveErrorCount => receiveErrorCount;

		public async Task<TcpClient> ConnectClient(IPEndPoint listenEndPoint)
		{
			var client = new TcpClient
			{
				NoDelay = false,
				ExclusiveAddressUse = false
			};

			await client.ConnectAsync(listenEndPoint.Address, listenEndPoint.Port);
			return client;
		}

		protected override string LogCat { get; }

		protected override void OnConnect(StratumClient<WorkerContextBase> client)
		{
			Interlocked.Increment(ref connectCount);

			connectSubject.OnNext(client);
		}

		protected override void OnDisconnect(string subscriptionId)
		{
			Interlocked.Increment(ref disconnectCount);

			disconnectSubject.OnNext(Unit.Default);
		}

		protected override Task OnRequestAsync(StratumClient<WorkerContextBase> client, Timestamped<JsonRpcRequest> request)
		{
			requestSubject.OnNext((client, request));

			return Task.FromResult(true);
		}

		protected override void OnReceiveComplete(StratumClient<WorkerContextBase> client)
		{
			Interlocked.Increment(ref receiveCompletedCount);

			receiveCompletedSubject.OnNext(client);

			base.OnReceiveComplete(client);
		}

		protected override void OnReceiveError(StratumClient<WorkerContextBase> client, Exception ex)
		{
			Interlocked.Increment(ref receiveErrorCount);

			receiveErrorSubject.OnNext(client);

			base.OnReceiveError(client, ex);
		}

		public void Dispose()
		{
			StopListeners();
		}
	}
}
