// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Freeform <c>----</c> atoms: which <c>data</c> types are text, and keys whose mean or name holds a colon.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4FreeformTests
{
	static void AssertValues (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	public void Mp4_ImplicitAndUtf8Data_AreText (int typeIndicator)
	{
		// Apple's well-known types: 0 is implicit (no type indicated), 1 is UTF-8. GetFreeformTag reads both.
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;
		file.Tag = new Mp4Tag ();
		file.Tag.SetAtoms ("----:com.apple.iTunes:Implicit",
			[new Mp4DataAtom (typeIndicator, BinaryData.FromStringUtf8 ("implicit"))]);
		var again = Mp4File.Read (file.Render (original).Span).File!;

		Assert.AreEqual ("implicit", again.GetFreeformTag ("com.apple.iTunes", "Implicit"));
		AssertValues (again.Tag!.GetCustomField ("Implicit"), "implicit");
	}

	[TestMethod]
	public void Mp4_NonTextData_IsNotAValue ()
	{
		var tag = new Mp4Tag ();
		tag.SetAtoms ("----:com.apple.iTunes:Blob", [
			new Mp4DataAtom (13, new BinaryData ([0xFF, 0xD8])),
			new Mp4DataAtom (1, BinaryData.FromStringUtf8 ("text"))]);

		AssertValues (tag.GetCustomField ("Blob"), "text");
	}

	[TestMethod]
	[DataRow ("a:b", "c")]
	[DataRow ("a", "b:c")]
	[DataRow ("x\\y", "z")]
	[DataRow ("a\\:b", "c\\d")]
	[DataRow (":lead", "trail:")]
	public void Mp4_FreeformMeanAndName_RoundTripWhateverTheyHold (string mean, string name)
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;

		file.SetFreeformTag (mean, name, "val");
		var rendered = file.Render (original).ToArray ();

		// mean and name are full boxes: size, type, version and flags, then the UTF-8 text.
		byte[] meanBox = [.. "mean"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes (mean)];
		byte[] nameBox = [.. "name"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes (name)];
		Assert.IsTrue (rendered.AsSpan ().IndexOf (meanBox) >= 0, "the mean is written as given");
		Assert.IsTrue (rendered.AsSpan ().IndexOf (nameBox) >= 0, "the name is written as given");
		var again = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("val", again.GetFreeformTag (mean, name));
		CollectionAssert.AreEqual (rendered, again.Render (rendered).ToArray (), "an unchanged save keeps the atom");
	}

	[TestMethod]
	public void Mp4_FreeformMeanWithColon_IsNotAnotherMeanAndName ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;

		file.SetFreeformTag ("a:b", "c", "one");
		file.SetFreeformTag ("a", "b:c", "two");
		var again = Mp4File.Read (file.Render (original).Span).File!;

		Assert.AreEqual ("one", again.GetFreeformTag ("a:b", "c"));
		Assert.AreEqual ("two", again.GetFreeformTag ("a", "b:c"));
	}
}
