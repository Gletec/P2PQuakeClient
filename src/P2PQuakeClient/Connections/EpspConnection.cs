﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace P2PQuakeClient.Connections
{
	public abstract class EpspConnection : IDisposable
	{
		protected TcpClient TcpClient { get; }
		protected NetworkStream Stream { get; set; }

		/// <summary>
		/// 接続完了 ただしTcpClientを渡した場合は流れてこない
		/// </summary>
		public event Action Connected;
		/// <summary>
		/// 切断
		/// </summary>
		public event Action Disconnected;

		byte[] ReceiveBuffer { get; }
		PacketSplitter Splitter { get; }

		Task ConnectionTask { get; set; }
		CancellationTokenSource TokenSource { get; }

		string Host { get; }
		int Port { get; }
		protected EpspConnection(string host, int port)
		{
			ReceiveBuffer = new byte[1024];
			Splitter = new PacketSplitter();
			TokenSource = new CancellationTokenSource();
			TcpClient = new TcpClient();
			Host = host;
			Port = port;
		}
		protected EpspConnection(TcpClient client)
		{
			ReceiveBuffer = new byte[1024];
			Splitter = new PacketSplitter();
			TokenSource = new CancellationTokenSource();
			TcpClient = client;
		}

		public void StartReceive()
		{
			if (ConnectionTask != null)
				throw new InvalidOperationException("すでに受信は開始済みです。");
			if (!TcpClient.Connected)
			{
				TcpClient.Connect(Host, Port);
				Connected?.Invoke();
			}
			ConnectionTask = new Task(ReceiveTask().Wait, TokenSource.Token, TaskCreationOptions.LongRunning);
			ConnectionTask.Start();
		}
		private async Task ReceiveTask()
		{
			try
			{
				TcpClient.NoDelay = true;

				Stream = TcpClient.GetStream();

				var count = 0;
				while (Stream.CanRead && (count = await Stream.ReadAsync(ReceiveBuffer, 0, ReceiveBuffer.Length, TokenSource.Token)) > 0)
				{
					foreach (var rawPacket in Splitter.ParseAndSplit(ReceiveBuffer, count))
					{
						//Console.WriteLine("Splitted: " + rawPacket);
						OnReceive(new EpspPacket(rawPacket));
					}
				}
			}
			catch (Exception ex) when (ex is TaskCanceledException || ex is IOException || ex is SocketException)
			{
			}
			catch (EpspException ex)
			{
				Console.WriteLine("Receive Packet Parse Exception: " + ex);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Receive General Exception: " + ex);
			}
			Disconnect();
		}
		protected abstract void OnReceive(EpspPacket packet);

		protected async Task SendPacket(EpspPacket packet)
		{
			try
			{
				if (!TcpClient.Connected || TokenSource.IsCancellationRequested)
				{
					Disconnect();
					return;
				}

				//Console.WriteLine("Send: " + packet.ToPacketString());
				byte[] buffer = Splitter.Encoding.GetBytes(packet.ToPacketString() + "\r\n");
				await Stream.WriteAsync(buffer, 0, buffer.Length);
			}
			catch (Exception ex) when (ex is IOException || ex is SocketException)
			{
				Disconnect();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Send General Exception: " + ex);
				Disconnect();
			}
		}


		public void Disconnect()
		{
			if (!TcpClient.Connected)
				return;
			TokenSource.Cancel();
			TcpClient.Close();
			Disconnected?.Invoke();
		}

		public void Dispose()
		{
			if (!TokenSource.IsCancellationRequested)
				Disconnect();
			TcpClient?.Dispose();
		}
	}
}
