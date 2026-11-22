// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Kit;

// Shallow versions of every depth case: the files are well formed, detected as their format,
// and the probe that the child process runs reports each step.
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Kit")]
public class DepthCaseTests
{
	public static IEnumerable<object[]> Cases => DepthCases.All.Select (c => new object[] { c.Name });

	[TestMethod]
	[DynamicData (nameof (Cases))]
	public void Build_Shallow_IsReadAsItsFormatWithATag (string name)
	{
		var depthCase = DepthCases.Find (name);
		var data = depthCase.Build (3);

		var result = MediaFile.ReadFromData (data, "/shallow" + depthCase.Extension);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (depthCase.Format, result.Format);
		Assert.IsNotNull (result.Tag);
	}

	[TestMethod]
	[DynamicData (nameof (Cases))]
	public void Probe_Shallow_ReadsEditsAndSavesThroughEveryEntryPoint (string name)
	{
		using var output = new StringWriter ();

		var code = DepthProbe.Run (DepthCases.Find (name), 3, output);

		var text = output.ToString ();
		Assert.AreEqual (0, code, text);
		Assert.Contains ("span=ok", text, text);
		Assert.Contains ("stream=ok", text, text);
		Assert.Contains ("path=ok", text, text);
		Assert.Contains ("save=ok", text, text);
		Assert.Contains ("async=ok", text, text);
		Assert.Contains ("reread=ok", text, text);
		Assert.IsTrue (DepthProbe.Passed (text), text);
	}

	[TestMethod]
	[DataRow ("x depth=3 span=ok(Mp4) path=fail(too deep)", true)]
	[DataRow ("x depth=3 span=ok(Mp4) save=ok reread=ok(Mp4)", true)]
	[DataRow ("x depth=3 span=THREW InvalidDataException: bad", false)]
	[DataRow ("x depth=3 save=ok reread=fail(Invalid)", false)]
	[DataRow ("x depth=3 save=ok reread=ok(Mp4) title-lost", false)]
	public void Passed_AcceptsRefusalsAndRejectsThrowsAndLostEdits (string line, bool passed)
	{
		Assert.AreEqual (passed, DepthProbe.Passed (line));
	}

	[TestMethod]
	public void MatroskaCues_PointAtTheCluster ()
	{
		// The save must have something to patch: every CueClusterPosition names the Cluster's offset.
		var data = DepthCases.Find ("matroska-cues").Build (2);
		var cluster = data.AsSpan ().IndexOf (new byte[] { 0x1F, 0x43, 0xB6, 0x75 });
		var segmentBody = data.AsSpan ().IndexOf (new byte[] { 0x18, 0x53, 0x80, 0x67 }) + 4 + 8;
		var position = data.AsSpan ().IndexOf (new byte[] { 0xF1, 0x01, 0, 0, 0, 0, 0, 0, 0x08 }) + 9;

		Assert.AreEqual ((ulong)(cluster - segmentBody), System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian (data.AsSpan (position)));
	}

	[TestMethod]
	public void Probe_AReaderThatThrows_ReportsTheStepAndFails ()
	{
		var broken = new DepthCase ("broken", MediaFormat.Mp4, ".m4a", "test only", _ => throw new InvalidOperationException ("builder"));
		using var output = new StringWriter ();

		var code = DepthProbe.Run (broken, 3, output);

		Assert.AreEqual (DepthProbe.ThrewExitCode, code);
		Assert.Contains ("InvalidOperationException", output.ToString ());
	}

	[TestMethod]
	public void Find_AnUnknownName_ListsTheKnownCases ()
	{
		var error = Assert.ThrowsExactly<ArgumentException> (() => DepthCases.Find ("nope"));

		Assert.Contains ("mp4-udta", error.Message);
	}

	[TestMethod]
	public void All_NamesAreUnique ()
	{
		var names = DepthCases.All.Select (c => c.Name).ToList ();

		CollectionAssert.AllItemsAreUnique (names);
	}
}
