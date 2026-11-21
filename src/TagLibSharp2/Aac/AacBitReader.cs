// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// Reads MSB-first bit fields (ISO/IEC 13818-7 §6, bslbf/uimsbf). A read past the end sets
/// <see cref="Overrun"/> and returns zero, so a caller checks once after a run of fields.
/// </summary>
internal ref struct AacBitReader
{
	readonly ReadOnlySpan<byte> _data;
	long _bit;

	public AacBitReader (ReadOnlySpan<byte> data)
	{
		_data = data;
		_bit = 0;
		Overrun = false;
	}

	/// <summary>Gets a value indicating whether a read ran past the data.</summary>
	public bool Overrun { get; private set; }

	/// <summary>Gets the bit position from the start of the data.</summary>
	public readonly long Position => _bit;

	/// <summary>Reads up to 32 bits.</summary>
	public uint Read (int count)
	{
		if (count <= 0)
			return 0;

		if (_bit + count > (long)_data.Length * 8) {
			Overrun = true;
			_bit = (long)_data.Length * 8;
			return 0;
		}

		uint value = 0;
		for (var i = 0; i < count; i++) {
			var b = _data[(int)(_bit >> 3)];
			var bit = (b >> (7 - (int)(_bit & 7))) & 1;
			value = (value << 1) | (uint)bit;
			_bit++;
		}

		return value;
	}

	/// <summary>Skips <paramref name="count"/> bits.</summary>
	public void Skip (long count)
	{
		if (count <= 0)
			return;

		if (_bit + count > (long)_data.Length * 8) {
			Overrun = true;
			_bit = (long)_data.Length * 8;
			return;
		}

		_bit += count;
	}

	/// <summary>Moves to the next byte boundary (byte_alignment()).</summary>
	public void AlignToByte ()
	{
		var rest = _bit & 7;
		if (rest != 0)
			Skip (8 - rest);
	}
}
