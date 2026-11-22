// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// What a child process did: its exit code, the ends of its output, and whether it was killed.
/// </summary>
public sealed record KitChildResult (int ExitCode, string Output, string Error, bool TimedOut, TimeSpan Elapsed)
{
	/// <summary>A one-paragraph account for an assertion message.</summary>
	public string Describe () =>
		(TimedOut ? $"child timed out after {Elapsed.TotalSeconds:F0}s" : $"child exited {ExitCode} after {Elapsed.TotalSeconds:F1}s")
		+ $"\nstdout:\n{Output}\nstderr:\n{Error}";
}

/// <summary>
/// Runs this test assembly again as a child process, so a crash in it fails one test, not the run.
/// </summary>
/// <remarks>
/// The child is <c>dotnet exec TagLibSharp2.Tests.dll &lt;command&gt; ...</c>, which enters <see cref="KitProgram"/>.
/// It runs with a 1 GiB GC heap limit (<c>DOTNET_GCHeapHardLimit</c>) so a runaway allocation is an
/// out-of-memory failure in the child instead of swapping the machine.
/// </remarks>
public static class KitChild
{
	/// <summary>The child's GC heap limit in bytes.</summary>
	public const long HeapLimit = 1L << 30;

	const int KeptLines = 40;

	/// <summary>Runs a <see cref="KitProgram"/> command in a child process and waits for it.</summary>
	public static KitChildResult Run (TimeSpan timeout, params string[] arguments)
	{
		ArgumentNullException.ThrowIfNull (arguments);
		var start = new ProcessStartInfo (DotnetHost ()) {
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		start.ArgumentList.Add ("exec");
		start.ArgumentList.Add (typeof (KitChild).Assembly.Location);
		foreach (var argument in arguments)
			start.ArgumentList.Add (argument);
		start.Environment["DOTNET_GCHeapHardLimit"] = HeapLimit.ToString ("X", System.Globalization.CultureInfo.InvariantCulture);
		start.Environment["DOTNET_DbgEnableMiniDump"] = "0";

		var output = new Tail ();
		var error = new Tail ();
		var watch = Stopwatch.StartNew ();
		using var process = new Process { StartInfo = start };
		process.OutputDataReceived += (_, e) => output.Add (e.Data);
		process.ErrorDataReceived += (_, e) => error.Add (e.Data);
		process.Start ();
		process.BeginOutputReadLine ();
		process.BeginErrorReadLine ();

		var exited = process.WaitForExit ((int)timeout.TotalMilliseconds);
		if (!exited) {
			try {
				process.Kill (entireProcessTree: true);
			} catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) {
				// It exited between the wait and the kill, or part of its tree could not be signalled.
			}
		}

		process.WaitForExit ();
		watch.Stop ();
		return new KitChildResult (exited ? process.ExitCode : -1, output.ToString (), error.ToString (), !exited, watch.Elapsed);
	}

	/// <summary>
	/// The dotnet host running this process: <c>DOTNET_HOST_PATH</c> when the SDK set it,
	/// else the one above the runtime directory, else <c>dotnet</c> on the PATH.
	/// </summary>
	static string DotnetHost ()
	{
		var fromSdk = Environment.GetEnvironmentVariable ("DOTNET_HOST_PATH");
		if (!string.IsNullOrEmpty (fromSdk) && File.Exists (fromSdk))
			return fromSdk;
		var name = RuntimeInformation.IsOSPlatform (OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
		var root = Path.GetFullPath (Path.Combine (RuntimeEnvironment.GetRuntimeDirectory (), "..", "..", ".."));
		var beside = Path.Combine (root, name);
		return File.Exists (beside) ? beside : name;
	}

	// Keeps the first and last lines: a stack overflow trace is long and its head and tail say what happened.
	sealed class Tail
	{
		readonly List<string> _head = [];
		readonly Queue<string> _tail = new ();
		readonly object _gate = new ();
		int _dropped;

		public void Add (string? line)
		{
			if (line is null)
				return;
			lock (_gate) {
				if (_head.Count < KeptLines) {
					_head.Add (line);
					return;
				}

				_tail.Enqueue (line);
				if (_tail.Count > KeptLines) {
					_tail.Dequeue ();
					_dropped++;
				}
			}
		}

		public override string ToString ()
		{
			lock (_gate) {
				var text = new StringBuilder ();
				foreach (var line in _head)
					text.AppendLine (line);
				if (_dropped > 0)
					text.AppendLine (System.Globalization.CultureInfo.InvariantCulture, $"... {_dropped} lines ...");
				foreach (var line in _tail)
					text.AppendLine (line);
				return text.ToString ();
			}
		}
	}
}
