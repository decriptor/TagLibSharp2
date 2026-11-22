// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// <see cref="ApeTagItem.TryMeasure"/> applies the rules of <see cref="ApeTagItem.Parse"/> from the first
/// <see cref="ApeTagItem.MeasureWindow"/> bytes, without copying the value.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public class ApeTagItemMeasureTests
{
	[TestMethod]
	public void TryMeasure_AgreesWithParse_OnRandomAndMutatedItems ()
	{
		var random = new Random (2026);
		var valid = new ApeTag { Title = "Title", Album = "Album" }.Render ().ToArray ()[..^32];
		for (var round = 0; round < 20_000; round++) {
			byte[] data;
			if (round % 2 == 0) {
				data = (byte[])valid.Clone ();
				for (var flips = random.Next (0, 4); flips > 0; flips--)
					data[random.Next (data.Length)] = (byte)random.Next (256);
				data = data[..random.Next (0, data.Length + 1)];
			} else {
				data = new byte[random.Next (0, 600)];
				random.NextBytes (data);
				if (data.Length >= 8) {
					BitConverter.GetBytes ((uint)random.Next (0, 700)).CopyTo (data, 0);
					for (var i = 8; i < Math.Min (data.Length, 8 + random.Next (0, 300)); i++)
						data[i] = (byte)random.Next (0x20, 0x7F);
				}
			}

			var parsed = ApeTagItem.Parse (data);
			var window = data.AsSpan (0, Math.Min (data.Length, ApeTagItem.MeasureWindow));
			var measured = ApeTagItem.TryMeasure (window, data.Length, out var consumed);
			Assert.AreEqual (parsed.IsSuccess, measured, $"round {round}: {parsed.Error}");
			if (measured)
				Assert.AreEqual (parsed.BytesConsumed, consumed, $"round {round}");
		}
	}

	[TestMethod]
	[DataRow ("ID3")]
	[DataRow ("tag")]
	[DataRow ("oggs")]
	[DataRow ("Mp+")]
	public void TryMeasure_RejectsReservedKeysInAnyCase (string key)
	{
		var item = new byte[8 + key.Length + 1 + 2];
		BitConverter.GetBytes (2u).CopyTo (item, 0);
		System.Text.Encoding.ASCII.GetBytes (key).CopyTo (item, 8);

		Assert.IsFalse (ApeTagItem.Parse (item).IsSuccess);
		Assert.IsFalse (ApeTagItem.TryMeasure (item, item.Length, out _));
	}
}
