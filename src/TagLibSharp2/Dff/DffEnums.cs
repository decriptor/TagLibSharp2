// DFF (DSDIFF) format enumerations
// Based on DSDIFF 1.5 specification

namespace TagLibSharp2.Dff;

/// <summary>
/// DFF compression types.
/// </summary>
public enum DffCompressionType
{
	/// <summary>Uncompressed DSD audio.</summary>
	Dsd,

	/// <summary>DST (Direct Stream Transfer) compressed audio.</summary>
	Dst,

	/// <summary>Unknown compression type.</summary>
	Unknown
}
