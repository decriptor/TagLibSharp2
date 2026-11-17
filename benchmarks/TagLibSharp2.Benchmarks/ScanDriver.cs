using System.Diagnostics;
using TagLibSharp2.Core;

namespace TagLibSharp2.Benchmarks;

static class ScanDriver
{
	public static int Run (string[] args)
	{
		var retain = args.Contains ("--retain");
		var directory = Path.Combine (Path.GetTempPath (), "tls-scan-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		var paths = new string[SampleFiles.Formats.Length];
		for (var i = 0; i < paths.Length; i++) {
			paths[i] = Path.Combine (directory, "song." + SampleFiles.Formats[i]);
			File.WriteAllBytes (paths[i], SampleFiles.Create (SampleFiles.Formats[i], 1024 * 1024));
			var check = MediaFile.Read (paths[i]);
			Console.WriteLine ($"{SampleFiles.Formats[i]} success={check.IsSuccess} title={check.Tag?.Title} error={check.Error}");
			if (!check.IsSuccess)
				return 1;
			if (check.File is IDisposable disposable)
				disposable.Dispose ();
		}

		var rounds = retain ? 40 : 300;
		foreach (var arg in args) {
			if (int.TryParse (arg, out var parsed) && parsed > 0)
				rounds = parsed;
		}
		var held = new List<MediaFileResult> (retain ? rounds * paths.Length : 0);
		var clock = Stopwatch.StartNew ();
		var reads = 0;
		for (var round = 0; round < rounds; round++) {
			foreach (var path in paths) {
				var result = MediaFile.Read (path);
				reads++;
				if (!result.IsSuccess) {
					Console.Error.WriteLine (result.Error);
					return 1;
				}

				if (retain)
					held.Add (result);
				else if (result.File is IDisposable disposable)
					disposable.Dispose ();
			}
		}

		clock.Stop ();
		Console.WriteLine ($"reads={reads} ms={clock.ElapsedMilliseconds} retained={held.Count}");
		if (!retain)
			return 0;

		Console.WriteLine ("READY");
		Thread.Sleep (TimeSpan.FromSeconds (20));
		return held.Count > 0 ? 0 : 1;
	}
}
