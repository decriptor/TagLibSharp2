// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class BatchProcessorTests
{
	static readonly string[] EmptyPaths = [];
	static readonly string[] SinglePath = ["file.mp3"];

	[TestMethod]
	public async Task ProcessAsync_ProcessesAllFiles ()
	{
		var paths = new[] { "file1.mp3", "file2.mp3", "file3.mp3" };

		var results = await BatchProcessor.ProcessAsync (
			paths,
			(path, ct) => Task.FromResult (path.ToUpperInvariant ()));

		Assert.HasCount (3, results);
		Assert.IsTrue (results.All (r => r.IsSuccess));
		Assert.AreEqual ("FILE1.MP3", results[0].Value);
		Assert.AreEqual ("FILE2.MP3", results[1].Value);
		Assert.AreEqual ("FILE3.MP3", results[2].Value);
	}

	[TestMethod]
	public async Task ProcessAsync_HandlesExceptions ()
	{
		var paths = new[] { "good.mp3", "bad.mp3", "good2.mp3" };

		var results = await BatchProcessor.ProcessAsync (
			paths,
			(path, ct) => {
				if (path.Contains ("bad", StringComparison.Ordinal))
					throw new InvalidOperationException ("Test error");
				return Task.FromResult (path);
			});

		Assert.HasCount (3, results);
		Assert.IsTrue (results[0].IsSuccess);
		Assert.IsFalse (results[1].IsSuccess);
		Assert.IsNotNull (results[1].Error);
		Assert.IsInstanceOfType<InvalidOperationException> (results[1].Error);
		Assert.IsTrue (results[2].IsSuccess);
	}

	[TestMethod]
	public async Task ProcessAsync_ReportsProgress ()
	{
		var paths = new[] { "file1.mp3", "file2.mp3", "file3.mp3" };
		var progressCount = 0;
		var allReported = new TaskCompletionSource<bool> ();

		var progress = new Progress<BatchProgress> (p => {
			if (Interlocked.Increment (ref progressCount) >= paths.Length)
				allReported.TrySetResult (true);
		});

		var results = await BatchProcessor.ProcessAsync (
			paths,
			(path, ct) => Task.FromResult (path),
			progress: progress);

		// Progress callbacks are posted asynchronously. Wait for them, and cancel the timer
		// if they arrive first so CA2027 does not leave a Task.Delay running.
		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (1));
		try {
			await allReported.Task.WaitAsync (timeout.Token);
		} catch (OperationCanceledException) when (timeout.IsCancellationRequested) {
		}

		Assert.HasCount (3, results);
		Assert.IsTrue (results.All (r => r.IsSuccess));
		// Verify progress was reported (may not always complete in time due to async nature)
		Assert.IsTrue (progressCount > 0, "Expected at least one progress report");
	}

	[TestMethod]
	public async Task ProcessAsync_LimitsParallelism ()
	{
		var paths = Enumerable.Range (1, 10).Select (i => $"file{i}.mp3").ToList ();
		var maxConcurrent = 0;
		var currentConcurrent = 0;
		var lockObj = new object ();

		var results = await BatchProcessor.ProcessAsync (
			paths,
			async (path, ct) => {
				lock (lockObj) {
					currentConcurrent++;
					if (currentConcurrent > maxConcurrent)
						maxConcurrent = currentConcurrent;
				}
				await Task.Delay (10, ct);
				lock (lockObj) {
					currentConcurrent--;
				}
				return path;
			},
			maxDegreeOfParallelism: 2);

		Assert.HasCount (10, results);
		Assert.IsLessThanOrEqualTo (maxConcurrent, 2, $"Max concurrent was {maxConcurrent}");
	}

	[TestMethod]
	public void Process_ProcessesAllFiles ()
	{
		var paths = new[] { "file1.mp3", "file2.mp3", "file3.mp3" };

		var results = BatchProcessor.Process (
			paths,
			path => path.ToUpperInvariant ());

		Assert.HasCount (3, results);
		Assert.IsTrue (results.All (r => r.IsSuccess));
	}

	[TestMethod]
	public void Process_HandlesExceptions ()
	{
		var paths = new[] { "good.mp3", "bad.mp3", "good2.mp3" };

		var results = BatchProcessor.Process (
			paths,
			path => {
				if (path.Contains ("bad", StringComparison.Ordinal))
					throw new InvalidOperationException ("Test error");
				return path;
			});

		Assert.HasCount (3, results);
		var failedResults = results.WhereFailed ().ToList ();
		Assert.HasCount (1, failedResults);
		Assert.AreEqual ("bad.mp3", failedResults[0].Path);
	}

	[TestMethod]
	public void BatchResult_SuccessHasValue ()
	{
		var result = BatchResult<string>.Success ("test.mp3", "result");

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.IsCancelled);
		Assert.AreEqual ("test.mp3", result.Path);
		Assert.AreEqual ("result", result.Value);
		Assert.IsNull (result.Error);
	}

	[TestMethod]
	public void BatchResult_FailureHasError ()
	{
		var error = new InvalidOperationException ("Test error");
		var result = BatchResult<string>.Failure ("test.mp3", error);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (result.IsCancelled);
		Assert.AreEqual ("test.mp3", result.Path);
		Assert.IsNull (result.Value);
		Assert.AreSame (error, result.Error);
	}

	[TestMethod]
	public void BatchResult_CancelledState ()
	{
		var result = BatchResult<string>.Cancelled ("test.mp3");

		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.IsCancelled);
		Assert.AreEqual ("test.mp3", result.Path);
		Assert.IsNull (result.Value);
		Assert.IsNull (result.Error);
	}

	[TestMethod]
	public void BatchProgress_CalculatesPercentage ()
	{
		var progress = new BatchProgress (5, 10, "file5.mp3");

		Assert.AreEqual (5, progress.Completed);
		Assert.AreEqual (10, progress.Total);
		Assert.AreEqual ("file5.mp3", progress.CurrentPath);
		Assert.AreEqual (50.0, progress.PercentComplete);
	}

	[TestMethod]
	public void BatchProgress_HandlesZeroTotal ()
	{
		var progress = new BatchProgress (0, 0, "");

		Assert.AreEqual (0.0, progress.PercentComplete);
	}

	[TestMethod]
	public void WhereSucceeded_FiltersCorrectly ()
	{
		var results = new[] {
			BatchResult<string>.Success ("a.mp3", "A"),
			BatchResult<string>.Failure ("b.mp3", new InvalidOperationException ()),
			BatchResult<string>.Success ("c.mp3", "C"),
			BatchResult<string>.Cancelled ("d.mp3")
		};

		var succeeded = results.WhereSucceeded ().ToList ();

		Assert.HasCount (2, succeeded);
		Assert.AreEqual ("a.mp3", succeeded[0].Path);
		Assert.AreEqual ("c.mp3", succeeded[1].Path);
	}

	[TestMethod]
	public void WhereFailed_FiltersCorrectly ()
	{
		var results = new[] {
			BatchResult<string>.Success ("a.mp3", "A"),
			BatchResult<string>.Failure ("b.mp3", new InvalidOperationException ()),
			BatchResult<string>.Cancelled ("c.mp3")
		};

		var failed = results.WhereFailed ().ToList ();

		Assert.HasCount (1, failed);
		Assert.AreEqual ("b.mp3", failed[0].Path);
	}

	[TestMethod]
	public void SuccessCount_CountsCorrectly ()
	{
		var results = new[] {
			BatchResult<string>.Success ("a.mp3", "A"),
			BatchResult<string>.Failure ("b.mp3", new InvalidOperationException ()),
			BatchResult<string>.Success ("c.mp3", "C")
		};

		Assert.AreEqual (2, results.SuccessCount ());
	}

	[TestMethod]
	public void FailureCount_CountsCorrectly ()
	{
		var results = new[] {
			BatchResult<string>.Success ("a.mp3", "A"),
			BatchResult<string>.Failure ("b.mp3", new InvalidOperationException ()),
			BatchResult<string>.Failure ("c.mp3", new InvalidOperationException ()),
			BatchResult<string>.Cancelled ("d.mp3")
		};

		Assert.AreEqual (2, results.FailureCount ());
	}

	[TestMethod]
	public async Task ProcessAsync_EmptyInput_ReturnsEmpty ()
	{
		var results = await BatchProcessor.ProcessAsync (
			EmptyPaths,
			(path, ct) => Task.FromResult (path));

		Assert.IsEmpty (results);
	}

	[TestMethod]
	public void Process_NullPaths_ThrowsArgumentNullException ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			BatchProcessor.Process<string> (null!, path => path));
	}

	[TestMethod]
	public void Process_NullOperation_ThrowsArgumentNullException ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			BatchProcessor.Process<string> (SinglePath, null!));
	}
}
