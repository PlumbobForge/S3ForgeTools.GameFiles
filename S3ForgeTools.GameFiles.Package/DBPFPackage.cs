using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using S3ForgeTools.Utils.Logging;

#nullable enable
namespace S3ForgeTools.GameFiles.Package;

public class DBPFPackage : IDisposable
{
	private static readonly ILog log = LogManager.GetLogger(nameof(DBPFPackage));

	private Stream? DataStore;

	private bool _IsEncrypted;

	public static readonly byte[] EncryptKey = new byte[96]
	{
		0, 0, 0, 22, 173, 32, 164, 122, 42, 238,
		220, 183, 77, 94, 169, 53, 91, 117, 38, 34,
		51, 186, 96, 88, 153, 162, 194, 136, 112, 59,
		89, 5, 138, 180, 247, 135, 234, 173, 5, 90,
		175, 140, 62, 24, 127, 218, 30, 212, 162, 133,
		172, 108, 95, 113, 100, 176, 205, 3, 114, 80,
		40, 88, 195, 99, 92, 31, 131, 189, 116, 246,
		107, 238, 191, 84, 70, 168, 220, 236, 223, 150,
		36, 225, 162, 99, 193, 251, 29, 255, 107, 5,
		186, 191, 64, 136, 95, 251
	};

	private bool disposed = false;

	public bool IsEncrypted => _IsEncrypted;

	public List<ResourceEntry> Resources { get; private set; }

	public string? FileName { get; private set; }

	public string? GUID { get; set; }

	public DBPFPackage()
	{
		Resources = new List<ResourceEntry>();
	}

	public DBPFPackage(Stream SourceStream)
		: this()
	{
		Import(SourceStream);
	}

	public DBPFPackage(string FileName)
		: this()
	{
		Import(FileName);
	}

	public void Close()
	{
		Dispose();
	}

	public void Clear()
	{
		if (DataStore != null)
		{
			DataStore.Close();
			DataStore = null;
		}
		foreach (ResourceEntry resource in Resources)
		{
			resource.Close();
		}
	}

	public void Export(string FileName)
	{
		Stream stream = File.Create(FileName);
		try
		{
			Export(stream);
		}
		finally
		{
			stream.Close();
		}
	}

	public void Export(Stream OutStream)
	{
		if (DataStore == null) return;
		DataStore.Seek(0L, SeekOrigin.Begin);
		DataStore.CopyTo(OutStream);
	}

	public static bool OptimizePackage(string packagePath, int level = 1)
	{
		if (!File.Exists(packagePath)) return false;

		string tempPath = packagePath + ".tmp_opt";
		try
		{
			using (var pkg = new DBPFPackage(packagePath))
			{
				if (pkg.Resources.Count == 0) return false;

				bool hasUncompressed = pkg.Resources.Any(r => !r.IsCompressed && r.Length >= 32);
				if (!hasUncompressed) return false;

				if (File.Exists(tempPath)) File.Delete(tempPath);

				using (var builder = new DBPFPackageBuilder(tempPath, pkg.IsEncrypted))
				{
					foreach (var res in pkg.Resources)
					{
						if (!res.IsCompressed && res.Length >= 32)
						{
							try { res.Compress(level); } catch { }
						}
						builder.AddResource(res);
					}
				}
			}

			if (File.Exists(tempPath))
			{
				File.Move(tempPath, packagePath, overwrite: true);
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			log.Warn($"Failed to optimize package {packagePath}: {ex.Message}");
			if (File.Exists(tempPath))
			{
				try { File.Delete(tempPath); } catch { }
			}
			return false;
		}
	}

	public void Import(string FileName)
	{
		DataStore = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
		try
		{
			this.FileName = FileName;
			Import();
		}
		catch (InvalidDataException)
		{
			DataStore.Close();
			throw;
		}
	}

	public void Import(Stream SourceStream)
	{
		FileName = "<Stream>";
		Import(SourceStream, UseBuffer: true);
	}

	public void Import(Stream SourceStream, bool UseBuffer)
	{
		if (UseBuffer)
		{
			DataStore = new MemoryStream();
			SourceStream.CopyTo(DataStore);
		}
		else
		{
			DataStore = SourceStream;
		}
		Import();
	}

	private void Import()
	{
		if (DataStore == null)
		{
			throw new InvalidOperationException("DataStore is not initialized");
		}
		DataStore.Seek(0L, SeekOrigin.Begin);
		Span<byte> header = stackalloc byte[96];
		DataStore.ReadExactly(header);

		string magic = Encoding.ASCII.GetString(header.Slice(0, 4));
		if (magic != "DBPF" && magic != "DBPP")
		{
			throw new InvalidDataException("Unknown Magic Number: " + magic);
		}
		if (magic == "DBPP")
		{
			_IsEncrypted = true;
			throw new InvalidDataException("DBPP Not Supported");
		}
		if (IsEncrypted)
		{
			for (int i = 0; i < 96; i++)
			{
				header[i] ^= EncryptKey[i];
			}
		}

		uint majorVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
		uint minorVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
		uint num3 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
		uint num4 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4));
		uint num5 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(20, 4));
		uint num6 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(24, 4));
		uint num7 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(28, 4));
		uint indexMajor = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(32, 4));
		uint recordCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4));
		uint headerOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(40, 4));
		uint indexSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(44, 4));
		uint num12 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(48, 4));
		uint num13 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(52, 4));
		uint num14 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(56, 4));
		uint indexMinor = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(60, 4));
		uint indexOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(64, 4));

		if (majorVersion != 2 || minorVersion != 0)
		{
			log.Warn($"Non-standard DBPF Version {majorVersion}.{minorVersion} -- attempting to parse package -- {FileName}");
		}
		if ((indexMajor != 0 && indexMajor != 7) || indexMinor != 3)
		{
			log.Warn($"Unknown Index Version {indexMajor}.{indexMinor}, Expected 0.3 -- {FileName}");
		}
		if (num3 != 0 || num4 != 0 || num5 != 0 || num6 != 0 || num7 != 0 || num12 != 0 || num14 != 0)
		{
			log.Warn($"Unused Header Value not set to 0 -- {FileName}");
		}
		if (headerOffset != 0 && headerOffset != indexOffset)
		{
			log.Warn($"Header Offset mismatch -- Not loadable by Game! -- {FileName}");
		}

		if (recordCount == 0)
		{
			log.Info("Empty Package -- No Index Entries");
			return;
		}

		byte[] indexBytes = System.Buffers.ArrayPool<byte>.Shared.Rent((int)indexSize);
		try
		{
			DataStore.Position = indexOffset;
			DataStore.ReadExactly(indexBytes, 0, (int)indexSize);
			ReadOnlySpan<byte> indexSpan = indexBytes.AsSpan(0, (int)indexSize);

			uint indexType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(0, 4));
			int constantFieldsSize = 0;
			if ((indexType & 1) == 1) constantFieldsSize += 4;
			if ((indexType & 2) == 2) constantFieldsSize += 4;
			if ((indexType & 4) == 4) constantFieldsSize += 4;
			if ((indexType & 8) == 8) constantFieldsSize += 4;

			int expectedSize = constantFieldsSize + 4 + ((32 - constantFieldsSize) * (int)recordCount);
			if (expectedSize != indexSize)
			{
				log.Warn($"DBPF Format Error, IndexType vs IndexSize mismatch: Actual Size {indexSize}, Calculated Size {expectedSize}, IndexType {indexType} -- {FileName}");
				throw new InvalidDataException("DBPF Format Error, IndexType vs IndexSize mismatch");
			}

			if ((indexType | 0xF) != 15)
			{
				log.Fatal("Import not implemented");
				throw new NotImplementedException();
			}

			int pos = 4;
			uint constType = 0;
			uint constGroup = 0;
			uint constInstHi = uint.MaxValue;
			uint constInstLo = uint.MaxValue;

			if ((indexType & 1) == 1) { constType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4; }
			if ((indexType & 2) == 2) { constGroup = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4; }
			if ((indexType & 4) == 4) { constInstHi = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4; }
			if ((indexType & 8) == 8) { constInstLo = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4; }

			Resources.Capacity = Math.Max(Resources.Capacity, Resources.Count + (int)recordCount);

			for (int i = 0; i < recordCount; i++)
			{
				uint type = (indexType & 1) != 0 ? constType : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4));
				if ((indexType & 1) == 0) pos += 4;

				uint group = (indexType & 2) != 0 ? constGroup : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4));
				if ((indexType & 2) == 0) pos += 4;

				uint instHi = (indexType & 4) != 0 ? constInstHi : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4));
				if ((indexType & 4) == 0) pos += 4;

				uint instLo = (indexType & 8) != 0 ? constInstLo : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4));
				if ((indexType & 8) == 0) pos += 4;

				ulong instance = instLo | ((ulong)instHi << 32);
				uint chunkOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4;
				uint chunkLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)) & 0x7FFFFFFF; pos += 4;
				uint resourceLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(indexSpan.Slice(pos, 4)); pos += 4;
				ushort isCompressed = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(indexSpan.Slice(pos, 2)); pos += 2;
				ushort unknown = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(indexSpan.Slice(pos, 2)); pos += 2;

				TGI_Key key = new TGI_Key(type, group, instance);
				ResourceEntry item = new ResourceEntry(DataStore, key, chunkOffset, (int)chunkLength, (int)resourceLength, isCompressed == ushort.MaxValue, IsEncrypted);
				Resources.Add(item);
			}
		}
		finally
		{
			System.Buffers.ArrayPool<byte>.Shared.Return(indexBytes);
		}
	}

	public void CopyTo(Stream OutputStream)
	{
		if (DataStore == null) return;
		DataStore.Position = 0L;
		DataStore.CopyTo(OutputStream);
	}

	public void CopyTo(string FileName)
	{
		Stream stream = File.Open(FileName, FileMode.OpenOrCreate, FileAccess.Write);
		try
		{
			CopyTo(stream);
		}
		finally
		{
			stream.Close();
		}
	}

	public ResourceEntry? GetResource(TGI_Key key)
	{
		for (int i = 0; i < Resources.Count; i++)
		{
			if (Resources[i].Key == key)
			{
				return Resources[i];
			}
		}
		return null;
	}

	public TGI_Key GetCompositionResource()
	{
		Dictionary<TGI_Key, int> compositionResources = GetCompositionResources();
		TGI_Key result = default;
		int num = -1;
		foreach (KeyValuePair<TGI_Key, int> item in compositionResources)
		{
			if (item.Value > num)
			{
				num = item.Value;
				result = item.Key;
			}
		}
		return result;
	}

	public Dictionary<TGI_Key, int> GetCompositionResources()
	{
		Dictionary<TGI_Key, int> dictionary = new Dictionary<TGI_Key, int>();
		foreach (ResourceEntry resource in Resources)
		{
			try
			{
				uint t = resource.Key.Type;
				if (t == 107542056 || t == 83396964 || t == 103306152)
				{
					dictionary[resource.Key] = 750;
				}
				else if (t == 3496170587u || t == 832458525 || t == 3571055589u || t == 55242443)
				{
					dictionary[resource.Key] = 500;
				}
				else if (t == 53690476 || t == 62078431 || t == 121612807)
				{
					dictionary[resource.Key] = 250;
				}
			}
			catch (ArgumentException)
			{
			}
		}
		return dictionary;
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
				Clear();
			}
			disposed = true;
		}
	}

	~DBPFPackage()
	{
		Dispose(disposing: false);
	}
}
