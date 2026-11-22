// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// Every entry point applies the same APEv2 key rules (https://wiki.hydrogenaud.io/index.php?title=APE_key):
/// creating an item, parsing one, and the custom field API.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public class ApeKeyRulesTests
{
	[TestMethod]
	[DataRow ("AB", true)]
	[DataRow ("A", false)]
	[DataRow ("", false)]
	[DataRow ("Title", true)]
	[DataRow ("ID3", false)]
	[DataRow ("tag", false)]
	[DataRow ("OGGS", false)]
	[DataRow ("mp+", false)]
	[DataRow ("ID3X", true)]
	[DataRow ("Tab\tKey", false)]
	[DataRow ("Tilde~", true)]
	[DataRow ("Del\u007F", false)]
	[DataRow ("Café", false)]
	public void KeyRules_AgreeAcrossEntryPoints (string key, bool valid)
	{
		Assert.AreEqual (valid, ApeTagItem.IsValidKey (key));
		Assert.AreEqual (valid, TryCreate (key));
		Assert.AreEqual (valid, new ApeTag ().SetCustomField (key, "v"));
		Assert.AreEqual (valid, ApeTagItem.Parse (RawItem (Encoding.Latin1.GetBytes (key))).IsSuccess);
		Assert.AreEqual (valid, Measures (RawItem (Encoding.Latin1.GetBytes (key))));
		if (valid)
			AssertCustomFieldRoundTrips (key);
	}

	[TestMethod]
	public void KeyRules_LengthBoundsAgreeAcrossEntryPoints ()
	{
		foreach (var (length, valid) in new[] { (255, true), (256, false) }) {
			var key = new string ('K', length);
			Assert.AreEqual (valid, ApeTagItem.IsValidKey (key));
			Assert.AreEqual (valid, TryCreate (key));
			Assert.AreEqual (valid, ApeTagItem.Parse (RawItem (Encoding.ASCII.GetBytes (key))).IsSuccess);
			Assert.AreEqual (valid, Measures (RawItem (Encoding.ASCII.GetBytes (key))));
			Assert.AreEqual (valid, new ApeTag ().SetCustomField (key, "v"));
			if (valid)
				AssertCustomFieldRoundTrips (key);
		}
	}

	/// <summary>The non-copying item check a tag placement uses, from its window only.</summary>
	static bool Measures (byte[] item) =>
		ApeTagItem.TryMeasure (item.AsSpan (0, Math.Min (item.Length, ApeTagItem.MeasureWindow)), item.Length, out _);

	/// <summary>
	/// A field SetCustomField accepts renders as an item that the measure and the tag parser accept, and reads back.
	/// </summary>
	static void AssertCustomFieldRoundTrips (string key)
	{
		var tag = new ApeTag ();
		Assert.IsTrue (tag.SetCustomField (key, "v1", "v2"));
		var rendered = tag.Render ().ToArray ();

		var parsed = ApeTag.Parse (rendered);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		CollectionAssert.AreEqual (V1V2, parsed.Tag!.GetCustomField (key).ToArray ());

		// Render writes the one item and a 32-byte footer (APEv2 tag layout, no header).
		var items = rendered.AsSpan (0, rendered.Length - 32);
		Assert.IsTrue (ApeTagItem.TryMeasure (items[..Math.Min (items.Length, ApeTagItem.MeasureWindow)], items.Length, out var consumed),
			$"the measure refuses the item SetCustomField wrote for '{key}'");
		Assert.AreEqual (items.Length, consumed);
	}

	static readonly string[] V1V2 = ["v1", "v2"];

	static bool TryCreate (string key)
	{
		try {
			ApeTagItem.CreateText (key, "v");
			return true;
		} catch (ArgumentException) {
			return false;
		}
	}

	static byte[] RawItem (byte[] key)
	{
		var item = new byte[8 + key.Length + 1 + 1];
		BinaryPrimitives.WriteUInt32LittleEndian (item, 1);
		key.CopyTo (item, 8);
		item[^1] = (byte)'v';
		return item;
	}
}
