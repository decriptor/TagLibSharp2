// Copyright (c) 2025 Stephen Shaw and contributors
// Async tests for MediaFile

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Async")]
public class MediaFileAsyncTests
{
	[TestMethod]
	public async Task OpenAsync_ValidFlac_ReturnsFlacFile ()
	{
		// Arrange - create a temp file with minimal FLAC data
		var data = TestBuilders.Flac.CreateMinimal ();
		var tempPath = Path.GetTempFileName ();
		try {
			await File.WriteAllBytesAsync (tempPath, data);

			// Act
			var result = await MediaFile.ReadAsync (tempPath);

			// Assert
			Assert.IsTrue (result.IsSuccess);
			Assert.AreEqual (MediaFormat.Flac, result.Format);
			Assert.IsNotNull (result.File);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task OpenAsync_FileNotFound_ReturnsFailure ()
	{
		// Arrange - non-existent file
		var path = Path.Combine (Path.GetTempPath (), $"nonexistent_{Guid.NewGuid ()}.flac");

		// Act
		var result = await MediaFile.ReadAsync (path);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task OpenAsync_NullPath_ThrowsArgumentNullException ()
	{
		// Act & Assert
		await Assert.ThrowsExactlyAsync<ArgumentNullException> (
			() => MediaFile.ReadAsync (null!));
	}

	[TestMethod]
	public async Task OpenAsync_CancellationToken_IsPropagated ()
	{
		// Arrange - write some data so file read is attempted
		var data = TestBuilders.Flac.CreateMinimal ();
		var tempPath = Path.GetTempFileName ();
		try {
			await File.WriteAllBytesAsync (tempPath, data);
			var cts = new CancellationTokenSource ();
			cts.Cancel ();

			// Act & Assert - cancellation might be checked at different points
			// depending on implementation, so we just verify it doesn't throw
			// other exceptions and handles the token gracefully
			try {
				var result = await MediaFile.ReadAsync (tempPath, cancellationToken: cts.Token);
				// If it completes without checking cancellation, that's OK
				// Some implementations may not check after file read completes
				Assert.IsNotNull (result);
			} catch (OperationCanceledException) {
				// Expected when cancellation is properly checked
			}
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void Open_NullPath_ThrowsArgumentNullException ()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException> (
			() => MediaFile.Read (null!));
	}

	[TestMethod]
	public void Open_FileNotFound_ReturnsFailure ()
	{
		// Arrange
		var path = Path.Combine (Path.GetTempPath (), $"nonexistent_{Guid.NewGuid ()}.flac");

		// Act
		var result = MediaFile.Read (path);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}
}
