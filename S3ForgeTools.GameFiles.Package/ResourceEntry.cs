using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace S3ForgeTools.GameFiles.Package;

public class ResourceEntry : IDisposable
{
	private long _ChunkOffset;

	private int _ChunkLength;

	private int _ResourceLength;

	private Stream DataStore;

	private bool _IsEncrypted;

	private bool disposed = false;

	public TGI_Key Key { get; private set; }

	public bool IsCompressed { get; private set; }

	public bool IsEncrypted => _IsEncrypted;

	public long Length => _ResourceLength;

	public int ChunkLength => _ChunkLength;

	public ResourceEntry(Stream SourceStream, TGI_Key ResourceKey, long ChunkOffset, int ChunkLength, int ResourceLength, bool IsCompressed, bool IsEncrypted)
	{
		DataStore = SourceStream;
		Key = ResourceKey;
		_ChunkOffset = ChunkOffset;
		_ChunkLength = ChunkLength;
		_ResourceLength = ResourceLength;
		this.IsCompressed = IsCompressed;
		_IsEncrypted = IsEncrypted;
	}

	public ResourceEntry(Stream SourceStream, TGI_Key ResourceKey)
	{
		DataStore = SourceStream;
		Key = ResourceKey;
		_ChunkOffset = 0L;
		_ChunkLength = (int)DataStore.Length;
		_ResourceLength = (int)DataStore.Length;
		IsCompressed = false;
		_IsEncrypted = false;
	}

	public void Close()
	{
		Dispose();
	}

	public void Clear()
	{
		throw new NotImplementedException();
	}

	public byte[] Read()
	{
		if (!IsCompressed)
		{
			return ReadRaw();
		}

		byte[] rentedRaw = ArrayPool<byte>.Shared.Rent(_ChunkLength);
		try
		{
			lock (DataStore)
			{
				DataStore.Position = _ChunkOffset;
				if (DataStore.Read(rentedRaw, 0, _ChunkLength) != _ChunkLength)
				{
					throw new InvalidDataException("Unexpected End of Data");
				}
			}

			byte[] output = GC.AllocateUninitializedArray<byte>(_ResourceLength);
			if (!Compression.Decompress(rentedRaw.AsSpan(0, _ChunkLength), output))
			{
				throw new InvalidDataException("Decompression Failure");
			}
			return output;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedRaw);
		}
	}

	public byte[] ReadRaw()
	{
		byte[] array = GC.AllocateUninitializedArray<byte>(_ChunkLength);
		lock (DataStore)
		{
			DataStore.Position = _ChunkOffset;
			if (DataStore.Read(array, 0, _ChunkLength) != _ChunkLength)
			{
				throw new InvalidDataException("Unexpected End of Data");
			}
		}
		return array;
	}

	public void CopyRawTo(Stream destination)
	{
		lock (DataStore)
		{
			DataStore.Position = _ChunkOffset;
			byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
			try
			{
				int remaining = _ChunkLength;
				while (remaining > 0)
				{
					int toRead = Math.Min(remaining, buffer.Length);
					int read = DataStore.Read(buffer, 0, toRead);
					if (read <= 0)
					{
						throw new InvalidDataException("Unexpected End of Data");
					}
					destination.Write(buffer, 0, read);
					remaining -= read;
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	public Stream GetStream(bool WantRaw = false)
	{
		if (WantRaw)
		{
			return new MemoryStream(ReadRaw(), writable: false);
		}
		return new MemoryStream(Read(), writable: false);
	}

	public void Export(Span<byte> destination)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), Key.Type);
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Key.Group);
		uint instanceLo = (uint)(Key.Instance & 0xFFFFFFFFu);
		uint instanceHi = (uint)((Key.Instance & 0xFFFFFFFF00000000uL) >> 32);
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), instanceHi);
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), instanceLo);
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), (uint)_ChunkOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), (uint)(_ChunkLength | int.MinValue));
		BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), (uint)Length);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(28, 2), IsCompressed ? ushort.MaxValue : (ushort)0);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(30, 2), 1);
	}

	public void Export(BinaryWriter Writer, uint IndexType)
	{
		if (IndexType != 0)
		{
			throw new ArgumentException();
		}
		Writer.Write(Key.Type);
		Writer.Write(Key.Group);
		uint value = (uint)(Key.Instance & 0xFFFFFFFFu);
		uint value2 = (uint)((Key.Instance & 0xFFFFFFFF00000000uL) >> 32);
		Writer.Write(value2);
		Writer.Write(value);
        Writer.Write((uint)_ChunkOffset);
		Writer.Write((uint)(_ChunkLength | int.MinValue));
		Writer.Write((uint)Length);
		if (IsCompressed)
		{
			Writer.Write(ushort.MaxValue);
		}
		else
		{
			Writer.Write((ushort)0);
		}
		Writer.Write((ushort)1);
	}

	public bool Compress(int level = 1)
	{
		return ChangeCompression(true, level);
	}

	public bool Decompress()
	{
		return ChangeCompression(false, 1);
	}

	public void ChangeStream(Stream NewStream)
	{
		DataStore = NewStream;
		IsCompressed = false;
		_ResourceLength = (int)DataStore.Length;
		_ChunkLength = (int)DataStore.Length;
		_ChunkOffset = 0L;
	}

	private bool ChangeCompression(bool Compress, int level)
	{
		if (Compress == IsCompressed)
		{
			return true;
		}
		if (Compress)
		{
			byte[] uncompressed = Read();
			int uncompressedLength = uncompressed.Length;
			if (Tiger.DBPFCompression.Compress(uncompressed, out byte[] compressed, level))
			{
				DataStore = new MemoryStream(compressed);
				IsCompressed = true;
				_ChunkOffset = 0L;
				_ChunkLength = compressed.Length;
				_ResourceLength = uncompressedLength;
				return true;
			}
			return false;
		}
		byte[] array = Read();
		ChangeStream(new MemoryStream(array));
		IsCompressed = false;
		_ResourceLength = array.Length;
		_ChunkLength = array.Length;
		return true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposed)
		{
			disposed = true;
		}
	}
}
