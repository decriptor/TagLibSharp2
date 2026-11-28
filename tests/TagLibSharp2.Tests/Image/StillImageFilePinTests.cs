// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

public partial class StillImageFileEntryTests
{
	[TestMethod]
	public void InvalidProperties_AreNotStored ()
	{
		var file = new StillImageFile ([1, 2, 3], MediaFormat.Svg, new ImageProperties (0, 0, 0, "SVG"), null);

		Assert.IsNull (file.ImageProperties);
	}

	[TestMethod]
	public void ReadFromFile_NullPathThrows_AndAMissingFileNamesItself ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => StillImageFile.ReadFromFile (null!));
		var missing = StillImageFile.ReadFromFile ("/no/such.jpg", new MockFileSystem ());

		Assert.IsFalse (missing.IsSuccess);
		Assert.AreEqual ("File not found: /no/such.jpg", missing.Error);
	}

	[TestMethod]
	public void SaveWithoutASourcePath_NamesTheMissingPath_OnBothOverloads ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);

		var sync = opened.File!.SaveToFile ();
		var async = opened.File.SaveToFileAsync ().GetAwaiter ().GetResult ();

		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", sync.Error);
		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", async.Error);
		Assert.ThrowsExactly<ArgumentNullException> (() => opened.File.SaveToFile ((string)null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => opened.File.SaveToFileAsync ((string)null!).GetAwaiter ().GetResult ());
	}

	[TestMethod]
	public void SaveToFile_UsesTheFileSystemItWasGiven ()
	{
		var bmp = Bmp (1, 1);
		var store = new MockFileSystem ();
		var opened = StillImageFile.Read (bmp);
		Assert.IsTrue (opened.IsSuccess, opened.Error);

		var saved = opened.File!.SaveToFile ("/out.bmp", store);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		CollectionAssert.AreEqual (bmp, store.ReadAllBytes ("/out.bmp"));

		var asyncSaved = opened.File.SaveToFileAsync ("/out-async.bmp", store).GetAwaiter ().GetResult ();

		Assert.IsTrue (asyncSaved.IsSuccess, asyncSaved.Error);
		CollectionAssert.AreEqual (bmp, store.ReadAllBytes ("/out-async.bmp"));
	}

	[TestMethod]
	public void SaveToFile_NullPathThrowsBeforeTheFileSystemIsUsed ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var files = FaultFileSystem.RootDirectory ();

		var sync = Assert.ThrowsExactly<ArgumentNullException> (() => opened.File!.SaveToFile ((string)null!, files));
		var async = Assert.ThrowsExactly<ArgumentNullException> (
			() => opened.File!.SaveToFileAsync ((string)null!, files).GetAwaiter ().GetResult ());

		Assert.AreEqual ("path", sync.ParamName);
		Assert.AreEqual ("path", async.ParamName);
		Assert.AreEqual (0, files.Calls);
	}

	[TestMethod]
	public void Save_SecondWriteReusesTheRentedBuffer ()
	{
		var bmp = Bmp (1, 1);
		var memory = new MockFileSystem ();
		var opened = StillImageFile.Read (bmp);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsTrue (opened.File!.SaveToFile ("/a.bmp", memory).IsSuccess);
		FileWriteResult again = default;
		Bounds.AssertAllocatesAtMost (32768, () => again = opened.File.SaveToFile ("/b.bmp", memory), "held save");
		Assert.IsTrue (again.IsSuccess, again.Error);

		var stored = new MockFileSystem ();
		stored.AddFile ("/in.bmp", bmp);
		var fromFile = StillImageFile.ReadFromFile ("/in.bmp", stored);
		Assert.IsTrue (fromFile.IsSuccess, fromFile.Error);
		Assert.IsTrue (fromFile.File!.SaveToFile ("/out1.bmp", stored).IsSuccess);
		FileWriteResult sourceAgain = default;
		Bounds.AssertAllocatesAtMost (32768, () => sourceAgain = fromFile.File.SaveToFile ("/out2.bmp", stored), "source save");
		Assert.IsTrue (sourceAgain.IsSuccess, sourceAgain.Error);
	}

	[TestMethod]
	public void EmptySourcePath_IsAMissingPath ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.SetSource ("", null);

		var saved = opened.File.SaveToFile ();

		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", saved.Error);
	}

	[TestMethod]
	public void SpliceFailure_DisposesTheSourceItOpened ()
	{
		var png = Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]));
		var fs = new ProbeFileSystem { ArmOpenTracking = true };
		fs.Add ("/a.png", png);
		var opened = StillImageFile.ReadFromFile ("/a.png", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Tag!.Title = "new";

		var saved = opened.File.SaveToFile ("/b.jpg", fs);

		Assert.IsFalse (saved.IsSuccess, saved.Error);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen!.WasDisposed);
	}

	[TestMethod]
	public void ReadResult_DefaultIsNotSuccess_AndFailuresDifferByError ()
	{
		Assert.IsFalse (default (StillImageFileReadResult).IsSuccess);
		Assert.IsFalse (StillImageFileReadResult.Failure ("a").Equals (StillImageFileReadResult.Failure ("b")));
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsFalse (opened.Equals (StillImageFile.Read (Bmp (2, 2))));
	}

	[TestMethod]
	public void ReadFromFile_NamesAnIoFailure ()
	{
		var broken = FaultFileSystem.Io (new IOException ("disk"));
		var sync = StillImageFile.ReadFromFile ("/a.jpg", broken);
		var async = StillImageFile.ReadFromFileAsync ("/a.jpg", broken).GetAwaiter ().GetResult ();

		Assert.AreEqual ("Failed to read file: disk", sync.Error);
		Assert.AreEqual ("Failed to read file: disk", async.Error);

		var denied = StillImageFile.ReadFromFile ("/a.jpg", FaultFileSystem.Io (new UnauthorizedAccessException ("no")));
		Assert.AreEqual ("Access denied: no", denied.Error);
	}
}
