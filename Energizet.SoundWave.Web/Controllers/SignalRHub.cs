using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NAudio.Wave;

namespace Energizet.SoundWave.Web.Controllers;

public class BackgroundWorker : IHostedService
{
	public IDictionary<string, ISingleClientProxy> Clients { get; set; } =
		new ConcurrentDictionary<string, ISingleClientProxy>();

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Task.Run(() =>
		{
			var waveIn = new WaveInEvent();
			waveIn.WaveFormat = new WaveFormat(60000, 32, 1);
			waveIn.BufferMilliseconds = 16;
			waveIn.DataAvailable += async (sender, args) =>
			{
				if (cancellationToken.IsCancellationRequested)
				{
					waveIn.StopRecording();
					return;
				}

				var a = GetBits32(args.Buffer, args.BytesRecorded)
					.Select(item => new Complex(item, 0))
					.ToList();
				a = Resize(a);
				await SendWave(Normalize(a), cancellationToken);
				FFTInPlaceFast(a);
				await SendFFT(Normalize(a), cancellationToken);
			};
			waveIn.StartRecording();
		}, cancellationToken);

		return Task.CompletedTask;
	}

	private static IEnumerable<long> GetBits8(IEnumerable<byte> buffer, int length)
	{
		return buffer.Take(length)
			.Select(item => ((long)item << 24) - ((long)1 << 31));
	}

	private static IEnumerable<long> GetBits16(IEnumerable<byte> buffer, int length)
	{
		using var enumerator = buffer.Take(length)
			.GetEnumerator();
		while (enumerator.MoveNext())
		{
			var res = (long)enumerator.Current;
			enumerator.MoveNext();
			res += ((long)enumerator.Current ^ (1 << 7)) << 8;
			yield return (res << 16) - ((long)1 << 31);
			//yield return ((current << 8) + tmp) ^ ((1 << 15) - 0);
		}
	}

	private static IEnumerable<long> GetBits32(IEnumerable<byte> buffer, int length)
	{
		using var enumerator = buffer.Take(length)
			.GetEnumerator();
		while (enumerator.MoveNext())
		{
			var res = (long)enumerator.Current;
			enumerator.MoveNext();
			res += ((long)enumerator.Current) << 8;
			enumerator.MoveNext();
			res += ((long)enumerator.Current) << 16;
			enumerator.MoveNext();
			res += ((long)enumerator.Current ^ (1 << 7)) << 24;
			yield return res - ((long)1 << 31);
			//yield return ((current << 8) + tmp) ^ ((1 << 15) - 0);
		}
	}

	private static int shift = 0;

	private static void GetWave(int[] buffer)
	{
		var angle = Math.PI / 180;
		for (var i = 0; i < buffer.Length; i++)
		{
			buffer[i] = (int)(Math.Sin((i + shift) * 2 * angle) * 1000);
		}

		shift = (shift + 4) % 360;
	}

	private static List<Complex> Resize(List<Complex> a)
	{
		var n = 1;
		while (n < a.Count)
		{
			n <<= 1;
		}

		return a.Concat(Enumerable.Range(0, n - a.Count).Select(_ => Complex.Zero)).ToList();
	}

	private static List<int> Normalize(List<Complex> a)
	{
		var result = a.Select(item => (int)Math.Round(item.Real)).ToList();

		return result;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private async Task SendWave(List<int> buffer, CancellationToken cancellationToken)
	{
		await Task.WhenAll(Clients.Values.Select(item =>
			item.SendAsync("SendWave", buffer, cancellationToken: cancellationToken)
		));
	}

	private async Task SendFFT(List<int> buffer, CancellationToken cancellationToken)
	{
		await Task.WhenAll(Clients.Values.Select(item =>
			item.SendAsync("SendFFT", buffer, cancellationToken: cancellationToken)
		));
	}

	private void FFT(List<Complex> a)
	{
		var n = a.Count;
		if (n == 1)
		{
			return;
		}

		var a0 = new List<Complex>();
		var a1 = new List<Complex>();
		for (var i = 0; 2 * i < n; i++)
		{
			a0.Add(a[2 * i]);
			a1.Add(a[2 * i + 1]);
		}

		FFT(a0);
		FFT(a1);

		var ang = 2 * Math.PI / n;
		var w = Complex.One;
		var wn = new Complex(Math.Cos(ang), Math.Sin(ang));
		for (var i = 0; 2 * i < n; i++)
		{
			a[i] = a0[i] + w * a1[i];
			a[i + n / 2] = a0[i] - w * a1[i];
			w *= wn;
		}
	}

	private void FFTInPlaceSlow(List<Complex> a)
	{
		var n = a.Count;
		var lg_n = 0;
		while ((1 << lg_n) < n)
		{
			lg_n++;
		}

		for (var i = 0; i < n; i++)
		{
			var reverse = Reverse(i, lg_n);
			if (i < reverse)
			{
				var t = a[i];
				a[i] = a[reverse];
				a[reverse] = t;
			}
		}

		for (var len = 2; len <= n; len <<= 1)
		{
			var ang = 2 * Math.PI / len;
			var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
			for (var i = 0; i < n; i += len)
			{
				var w = Complex.One;
				for (var j = 0; j < len / 2; j++)
				{
					var u = a[i + j];
					var v = a[i + j + len / 2] * w;
					a[i + j] = u + v;
					a[i + j + len / 2] = u - v;
					w *= wlen;
				}
			}
		}
	}

	private void FFTInPlaceFast(List<Complex> a)
	{
		var n = a.Count;

		for (int i = 1, j = 0; i < n; i++)
		{
			var bit = n >> 1;
			for (; (j & bit) > 0; bit >>= 1)
			{
				j ^= bit;
			}

			j ^= bit;

			if (i < j)
			{
				var t = a[i];
				a[i] = a[j];
				a[j] = t;
			}
		}

		for (var len = 2; len <= n; len <<= 1)
		{
			var ang = 2 * Math.PI / len;
			var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
			for (var i = 0; i < n; i += len)
			{
				var w = Complex.One;
				for (var j = 0; j < len / 2; j++)
				{
					var u = a[i + j];
					var v = a[i + j + len / 2] * w;
					a[i + j] = u + v;
					a[i + j + len / 2] = u - v;
					w *= wlen;
				}
			}
		}
	}

	private int Reverse(in int num, in int lg_n)
	{
		var res = 0;
		for (var i = 0; i < lg_n; i++)
		{
			if ((num & (1 << i)) > 0)
			{
				res |= 1 << (lg_n - 1 - i);
			}
		}

		return res;
	}
}

public class SignalRHub : Hub
{
	private readonly BackgroundWorker _worker;

	public SignalRHub(BackgroundWorker worker)
	{
		_worker = worker;
	}

	// GET
	public async Task SendMessage(string user, string message)
	{
		await Clients.All.SendAsync("ReceiveMessage", user, message);
	}

	public override async Task OnConnectedAsync()
	{
		_worker.Clients[Context.ConnectionId] = Clients.Caller;
		await base.OnConnectedAsync();
	}

	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		_worker.Clients.Remove(Context.ConnectionId);
		await base.OnDisconnectedAsync(exception);
	}
}