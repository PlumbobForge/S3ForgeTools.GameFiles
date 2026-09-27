using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace S3ForgeTools.GameFiles;

public static class EndianExtension
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ushort Swap(this ushort inValue)
	{
		return BinaryPrimitives.ReverseEndianness(inValue);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint Swap(this uint inValue)
	{
		return BinaryPrimitives.ReverseEndianness(inValue);
	}
}
