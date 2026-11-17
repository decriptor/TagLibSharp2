using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using TagLibSharp2.Core;

namespace TagLibSharp2.Benchmarks;

/// <summary>
/// One <see cref="MediaFile.Read(string)"/> of a file whose payload is 1 MiB.
/// The number to watch is Allocated: a windowed read stays far under that payload.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage ("Performance", "CA1822", Justification = "BenchmarkDotNet requires instance methods")]
public class MediaFileReadBenchmark
{
	const int PayloadBytes = 1024 * 1024;

	[Params ("mp3", "wav", "flac")]
	public string Format { get; set; } = "";

	string _directory = "";
	string _path = "";

	[GlobalSetup]
	public void Setup ()
	{
		_directory = Path.Combine (Path.GetTempPath (), "tls-bench-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		_path = Path.Combine (_directory, "song." + Format);
		File.WriteAllBytes (_path, SampleFiles.Create (Format, PayloadBytes));
	}

	[GlobalCleanup]
	public void Cleanup ()
	{
		if (Directory.Exists (_directory))
			Directory.Delete (_directory, true);
	}

	[Benchmark]
	public int Read ()
	{
		var result = MediaFile.Read (_path);
		var mark = result.IsSuccess ? (result.Tag?.Title?.Length ?? 0) + 1 : 0;
		if (result.File is IDisposable disposable)
			disposable.Dispose ();
		return mark;
	}
}
