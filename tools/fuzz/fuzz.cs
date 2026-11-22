#!/usr/bin/env dotnet
#:project ../../src/TagLibSharp2/TagLibSharp2.csproj
#:include ../Shared/ToolSupport.cs
#:property PublishAot=false
#:property AnalysisLevel=latest-recommended
#:property NoWarn=$(NoWarn);CA1031;CA1303;CA1305;CA1307;CA1310;CA1515;CA1812;CA1849;CA2007;CA5394;CS1591
// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Truncation and byte-flip fuzz over real files: every input goes through MediaFile.ReadFromData
// (span), MediaFile.Read (stream) and, when it reads, a no-op save. Prints every exception with
// the cut length or flipped offset, and every case slower than --slow-ms. Exit code 1 if anything threw.
//
//   dotnet run tools/fuzz/fuzz.cs -- [--lib <worktree|csproj>] [--cuts N] [--flips N] [--seed N]
//       [--slow-ms N] [--no-save] [--all] [--max-mb N] <file|dir>...
//
// A stack overflow ends this process: run NestingDepthTests for depth. See tools/README.md.

using System.Diagnostics;
using TagLibSharp2.Core;

var fromLibrary = ToolSupport.RunAgainstLibrary (args);
if (fromLibrary is int code)
	return code;

var options = args.ToList ();
var cuts = (int)ToolSupport.Option (options, "--cuts", 400);
var flips = (int)ToolSupport.Option (options, "--flips", 400);
var seed = (int)ToolSupport.Option (options, "--seed", 1);
var slowMs = ToolSupport.Option (options, "--slow-ms", 2000);
var maxBytes = ToolSupport.Option (options, "--max-mb", 32) * 1024 * 1024;
var save = !ToolSupport.Flag (options, "--no-save");
var all = ToolSupport.Flag (options, "--all");
var files = ToolSupport.MediaFiles (options, all, maxBytes);
if (files.Count == 0) {
	Console.Error.WriteLine ($"no media files: pass files or directories, or set {ToolSupport.CorpusVariable}");
	return 2;
}

long totalCases = 0, totalThrows = 0, totalSlow = 0;
foreach (var path in files) {
	var original = File.ReadAllBytes (path);
	var random = new Random (seed);
	var name = Path.GetFileName (path);
	long cases = 0, throws = 0, slow = 0;

	foreach (var length in CutLengths (original.Length, cuts))
		Run ($"cut {length}", original.AsSpan (0, length).ToArray ());

	// Half the flips land in the first and last 64 KiB, where headers and tags live.
	var data = (byte[])original.Clone ();
	for (var i = 0; i < flips && original.Length > 0; i++) {
		var offset = i % 2 == 0 || original.Length <= 128 * 1024
			? random.Next (original.Length)
			: random.Next (2) == 0 ? random.Next (64 * 1024) : original.Length - 1 - random.Next (64 * 1024);
		var was = data[offset];
		var now = (random.Next (4)) switch { 0 => (byte)0x00, 1 => (byte)0xFF, 2 => (byte)(was ^ 0x01), _ => (byte)(was ^ 0x80) };
		if (now == was)
			now = (byte)~was;
		data[offset] = now;
		Run ($"flip {offset} 0x{was:X2}->0x{now:X2}", data);
		data[offset] = was;
	}

	Console.WriteLine ($"{name}\tbytes={original.Length}\tcases={cases}\tthrows={throws}\tslow={slow}");
	totalCases += cases;
	totalThrows += throws;
	totalSlow += slow;

	void Run (string label, byte[] input)
	{
		cases++;
		var watch = Stopwatch.StartNew ();
		var step = "span";
		try {
			var span = MediaFile.ReadFromData (input, path);
			(span.File as IDisposable)?.Dispose ();

			step = "stream";
			using var stream = new MemoryStream (input, writable: false);
			var streamed = MediaFile.Read (stream, path);

			if (save && streamed.IsSuccess && streamed.File is not null) {
				step = "save";
				_ = ToolSupport.Save (streamed.File, input, "/out" + Path.GetExtension (path), new MemoryFiles ());
			}

			(streamed.File as IDisposable)?.Dispose ();
		} catch (Exception ex) {
			throws++;
			var frame = ex.StackTrace?.Split ('\n').FirstOrDefault (l => l.Contains ("TagLibSharp2.", StringComparison.Ordinal))?.Trim ();
			Console.WriteLine ($"{name}\t{label}\t{step}\t{ex.GetType ().Name}: {ToolSupport.Cell (ex.Message)}\t{frame}");
		}

		if (watch.ElapsedMilliseconds > slowMs) {
			slow++;
			Console.WriteLine ($"{name}\t{label}\tSLOW {watch.ElapsedMilliseconds} ms");
		}
	}
}

Console.WriteLine ($"total\tfiles={files.Count}\tcases={totalCases}\tthrows={totalThrows}\tslow={totalSlow}");
return totalThrows > 0 ? 1 : 0;

// Every length in the first and last 256 bytes, plus evenly spaced cuts between.
static IEnumerable<int> CutLengths (int length, int count)
{
	var lengths = new SortedSet<int> ();
	for (var i = 0; i <= Math.Min (256, length); i++) {
		lengths.Add (i);
		lengths.Add (length - i);
	}

	for (var i = 1; i < count; i++)
		lengths.Add ((int)((long)length * i / count));
	return lengths;
}
