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
	}

	[TestMethod]
	public void KeyRules_LengthBoundsAgreeAcrossEntryPoints ()
	{
		foreach (var (length, valid) in new[] { (255, true), (256, false) }) {
			var key = new string ('K', length);
			Assert.AreEqual (valid, ApeTagItem.IsValidKey (key));
			Assert.AreEqual (valid, TryCreate (key));
			Assert.AreEqual (valid, ApeTagItem.Parse (RawItem (Encoding.ASCII.GetBytes (key))).IsSuccess);
		}
	}

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
