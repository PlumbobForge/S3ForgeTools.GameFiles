using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using S3ForgeTools.Utils.Logging;

namespace S3ForgeTools.GameFiles.Package;

public class DBPFPackageBuilder : IDisposable
{
	private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType.ToString());

	private Stream DataStore;

	private bool _IsEncrypted;

	private bool disposed = false;

	private long _currentLength;

	public bool IsEncrypted => _IsEncrypted;

	public bool IsModified { get; private set; }

	public long PackageSize => _currentLength;

	public List<ResourceEntry> Resources { get; private set; }

	public DBPFPackageBuilder(string FileName, bool IsEncrypted = false)
	{
		Resources = new List<ResourceEntry>();
		_IsEncrypted = IsEncrypted;
		DataStore = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 262144);
		GenerateBlank();
	}

	public DBPFPackageBuilder(Stream SourceStream, bool IsEncrypted)
	{
		Resources = new List<ResourceEntry>();
		_IsEncrypted = IsEncrypted;
		DataStore = SourceStream;
		GenerateBlank();
	}

	public void Close()
	{
		Dispose();
	}

	private void CloseState()
	{
		if (IsModified)
		{
			GenerateIndex();
		}
		foreach (ResourceEntry resource in Resources)
		{
			resource.Close();
		}
		Resources.Clear();
		DataStore?.Close();
		DataStore = null!;
	}

	public void AddResource(ResourceEntry Resource)
	{
		if (IsEncrypted != Resource.IsEncrypted)
		{
			throw new InvalidDataException("Encryption State Mismatch");
		}
		long offset = _currentLength;
		int chunkLength = Resource.ChunkLength;
		int resourceLength = (int)Resource.Length;

		Resource.CopyRawTo(DataStore);
		_currentLength += chunkLength;

		ResourceEntry item = new ResourceEntry(DataStore, Resource.Key, offset, chunkLength, resourceLength, Resource.IsCompressed, Resource.IsEncrypted);
		Resources.Add(item);
		IsModified = true;
	}

	private void GenerateBlank()
	{
		byte[] array = new byte[96];
		BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(array, 0, 96, writable: true));
		binaryWriter.Write(new char[4] { 'D', 'B', 'P', 'F' });
		binaryWriter.Write(2u);
		binaryWriter.BaseStream.Position = 60L;
		binaryWriter.Write(3u);
		if (IsEncrypted)
		{
			for (int i = 0; i < 96; i++)
			{
				array[i] ^= DBPFPackage.EncryptKey[i];
			}
		}
		DataStore.Write(array, 0, 96);
		_currentLength = 96;
		binaryWriter.Close();
	}

	private void GenerateIndex()
	{
		uint indexOffset = (uint)_currentLength;
		int indexSize = 4 + (Resources.Count * 32);
		byte[] indexBuffer = ArrayPool<byte>.Shared.Rent(indexSize);

		try
		{
			BinaryPrimitives.WriteUInt32LittleEndian(indexBuffer.AsSpan(0, 4), 0u);
			int pos = 4;
			foreach (ResourceEntry resource in Resources)
			{
				resource.Export(indexBuffer.AsSpan(pos, 32));
				pos += 32;
			}
			DataStore.Write(indexBuffer, 0, indexSize);
			_currentLength += indexSize;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(indexBuffer);
		}

		using var binaryWriter = new BinaryWriter(DataStore, System.Text.Encoding.Default, leaveOpen: true);
		binaryWriter.BaseStream.Position = 36L;
		if (IsEncrypted)
		{
			binaryWriter.Write((uint)((ulong)Resources.Count ^ 0x5A05ADEAuL));
		}
		else
		{
			binaryWriter.Write((uint)Resources.Count);
		}

		binaryWriter.BaseStream.Position = 44L;
		if (IsEncrypted)
		{
			binaryWriter.Write((uint)indexSize ^ 0xD41EDA7Fu);
		}
		else
		{
			binaryWriter.Write((uint)indexSize);
		}

		binaryWriter.BaseStream.Position = 64L;
		if (IsEncrypted)
		{
			binaryWriter.Write(indexOffset ^ 0xBD831F5Cu);
		}
		else
		{
			binaryWriter.Write(indexOffset);
		}

		IsModified = false;
		binaryWriter.Flush();
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
			if (disposing)
			{
				CloseState();
			}
			disposed = true;
		}
	}

	~DBPFPackageBuilder()
	{
		Dispose(disposing: false);
	}
}
