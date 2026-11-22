// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// The entry point of the test assembly when <see cref="KitChild"/> runs it as a child process.
/// </summary>
/// <remarks>
/// The test project sets <c>GenerateProgramFile</c> to false so this Main replaces the empty one the
/// test SDK generates. The test host never calls it; <c>dotnet exec TagLibSharp2.Tests.dll</c> does.
/// <list type="bullet">
/// <item><c>kit-depth &lt;case&gt; &lt;depth&gt;</c>: runs <see cref="DepthProbe"/> on a 1 MiB stack.</item>
/// <item><c>kit-selftest ok|overflow|stack|throw|allocate|hang</c>: proves the runner reports each way a child ends.</item>
/// </list>
/// </remarks>
public static class KitProgram
{
	/// <summary>Exit code for a command line this program does not know.</summary>
	public const int UsageExitCode = 64;

	/// <summary>
	/// The stack each case runs on: the Windows main-thread default, the smallest of the CI platforms,
	/// so a pass here holds on every platform.
	/// </summary>
	public const int StackBytes = 1024 * 1024;

	/// <summary>Dispatches a kit command.</summary>
	public static int Main (string[] args)
	{
		// A solution build can leave the project reference out of TagLibSharp2.Tests.deps.json.
		// The test host probes the output folder anyway; a plain `dotnet exec` does not, so do it here,
		// before Run touches a library type.
		System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => {
			var candidate = Path.Combine (AppContext.BaseDirectory, name.Name + ".dll");
			return File.Exists (candidate) ? context.LoadFromAssemblyPath (candidate) : null;
		};
		return Run (args);
	}

	[System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	static int Run (string[] args)
	{
		ArgumentNullException.ThrowIfNull (args);
		if (args.Length == 3 && args[0] == "kit-depth"
			&& int.TryParse (args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var depth))
			return OnSmallStack (() => DepthProbe.Run (DepthCases.Find (args[1]), depth, Console.Out));
		if (args.Length == 2 && args[0] == "kit-selftest")
			return OnSmallStack (() => SelfTest (args[1]));

		Console.Error.WriteLine ("usage: TagLibSharp2.Tests kit-depth <case> <depth> | kit-selftest ok|overflow|stack|throw|allocate|hang");
		Console.Error.WriteLine ($"cases: {string.Join (", ", DepthCases.All.Select (c => c.Name))}");
		return UsageExitCode;
	}

	static int OnSmallStack (Func<int> body)
	{
		var code = 0;
		var thread = new Thread (() => code = body (), StackBytes);
		thread.Start ();
		thread.Join ();
		return code;
	}

	static int SelfTest (string mode)
	{
		switch (mode) {
			case "ok":
				Console.WriteLine ("selftest ok");
				return 0;
			case "overflow":
				return Recurse (0, int.MaxValue);
			case "stack":
				// Each frame holds at least 1 KiB, so the depth reached before the runtime reports the stack
				// nearly full is a lower bound on the stack size in KiB, whatever the JIT tier.
				Console.WriteLine ($"stack-kib={MeasureStack (0)}");
				return 0;
			case "throw":
				throw new InvalidOperationException ("selftest throw");
			case "allocate":
				var held = new List<byte[]> ();
				while (true)
					held.Add (new byte[64 * 1024 * 1024]);
			case "hang":
				Thread.Sleep (Timeout.Infinite);
				return 0;
			default:
				Console.Error.WriteLine ($"unknown selftest '{mode}' (kit-depth for cases)");
				return UsageExitCode;
		}
	}

	[System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	static int MeasureStack (int depth)
	{
		Span<byte> pad = stackalloc byte[1024];
		pad[^1] = (byte)depth;
		if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack ())
			return depth;
		var deepest = MeasureStack (depth + 1);
		return pad[^1] == (byte)depth ? deepest : -1;
	}

	// Each frame keeps a value live after the call, so the JIT cannot turn this into a loop.
	static int Recurse (int depth, int limit) => depth == limit ? depth : Recurse (depth + 1, limit) + 1;
}
