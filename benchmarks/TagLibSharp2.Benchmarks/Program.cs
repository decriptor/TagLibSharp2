using BenchmarkDotNet.Running;
using TagLibSharp2.Benchmarks;

if (args.Length > 0 && args[0] == "--scan")
	return ScanDriver.Run (args);

BenchmarkSwitcher.FromAssembly (typeof (Program).Assembly).Run (args);
return 0;
