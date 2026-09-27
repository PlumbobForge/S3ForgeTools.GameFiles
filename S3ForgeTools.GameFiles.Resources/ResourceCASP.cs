using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace S3ForgeTools.GameFiles.Resources;

public class ResourceCASP
{
	public string Name { get; private set; } = string.Empty;

	public uint ClothingType { get; private set; }

	public uint TypeFlags { get; private set; }

	public uint AgeGender { get; private set; }

	public uint Category { get; private set; }

	public ResourceCASP(string Filename)
	{
		byte[] buffer = File.ReadAllBytes(Filename);
		Import(buffer);
	}

	public ResourceCASP(Stream Source)
	{
		using var ms = new MemoryStream();
		Source.CopyTo(ms);
		Import(ms.ToArray());
	}

	public ResourceCASP(byte[] buffer)
	{
		Import(buffer);
	}

	public ResourceCASP(ReadOnlySpan<byte> span)
	{
		Import(span);
	}

	private void Import(ReadOnlySpan<byte> span)
	{
		if (span.Length < 16) return;

		int pos = 0;
		uint version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
		pos += 4; // skip unknown uint
		uint strCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;

		for (int i = 0; i < strCount; i++)
		{
			if (pos + 4 > span.Length) return;
			uint charCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
			int byteLen = (int)(charCount * 2);
			pos += byteLen; // skip unused string without allocating
			pos += 4; // skip unknown uint
		}

		if (pos + 2 > span.Length) return;
		ushort nameByteCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos, 2)); pos += 2;

		if (pos + nameByteCount > span.Length) return;
		Name = Encoding.Unicode.GetString(span.Slice(pos, nameByteCount)); pos += nameByteCount;

		if (pos + 20 > span.Length) return;
		pos += 4; // skip float
		ClothingType = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
		TypeFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
		AgeGender = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
		Category = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4)); pos += 4;
	}
}
