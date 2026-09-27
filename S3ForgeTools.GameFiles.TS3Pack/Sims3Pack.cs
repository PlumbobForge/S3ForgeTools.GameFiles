using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;
using System.Xml;
using S3ForgeTools.GameFiles.Package;

#nullable enable
namespace S3ForgeTools.GameFiles.TS3Pack;

public class Sims3Pack : IDisposable
{
	private Stream? DataStore;

	private bool disposed = false;

	public List<DBPFPackage> Packages { get; private set; }

	public Stream? Thumbnail { get; private set; }

	public List<Stream> Thumbnails { get; private set; }

	public XmlDocument? Manifest { get; private set; }

	public bool IsCorrupt { get; private set; }

	public bool IsEncrypted { get; private set; }

	public string? Type { get; private set; }

	public string? SubType { get; private set; }

	public Sims3Pack()
	{
		Packages = new List<DBPFPackage>();
		Thumbnails = new List<Stream>();
	}

	public Sims3Pack(Stream SourceStream)
		: this()
	{
		DataStore = SourceStream;
		Import();
	}

	public Sims3Pack(string FileName)
		: this()
	{
		IsEncrypted = false;
		DataStore = File.Open(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
		try
		{
			Import();
		}
		catch (InvalidDataException ex)
		{
			DataStore.Close();
			if (ex.Message == "DBPP Not Supported")
			{
				IsEncrypted = true;
			}
		}
		catch (XmlException)
		{
			DataStore.Close();
			IsCorrupt = true;
		}
		catch (EndOfStreamException)
		{
			DataStore.Close();
			IsCorrupt = true;
		}
	}

	private readonly struct PackagedFileInfo
	{
		public readonly string Name;
		public readonly long Offset;
		public readonly long Length;
		public readonly bool IsPackage;

		public PackagedFileInfo(string name, long offset, long length, bool isPackage)
		{
			Name = name;
			Offset = offset;
			Length = length;
			IsPackage = isPackage;
		}
	}

	private void Import()
	{
		if (DataStore == null)
		{
			throw new InvalidOperationException("DataStore is not initialized");
		}
		BinaryReader binaryReader = new BinaryReader(DataStore, Encoding.UTF8, leaveOpen: true);
		int magicLen = binaryReader.ReadInt32();
		if (magicLen <= 0 || magicLen > 64)
		{
			throw new InvalidDataException("Invalid Magic Length");
		}
		string text = new string(binaryReader.ReadChars(magicLen));
		if (text != "TS3Pack")
		{
			throw new InvalidDataException($"Invalid Magic: Expected [TS3Pack], Found [{text}]");
		}

		ushort version = binaryReader.ReadUInt16();
		int manifestLen = binaryReader.ReadInt32();

		byte[] manifestBytes = new byte[manifestLen];
		DataStore.ReadExactly(manifestBytes, 0, manifestLen);

		Manifest = new XmlDocument();
		using (var ms = new MemoryStream(manifestBytes, writable: false))
		{
			Manifest.Load(ms);
		}

		long dataStartPosition = DataStore.Position;

		XmlNode? rootNode = Manifest.DocumentElement;
		if (rootNode is XmlElement rootElem)
		{
			Type = rootElem.GetAttribute("Type");
			SubType = rootElem.GetAttribute("SubType");
		}

		var packagedFiles = new List<PackagedFileInfo>();
		XmlNodeList fileNodes = Manifest.GetElementsByTagName("PackagedFile");
		foreach (XmlNode node in fileNodes)
		{
			if (node is XmlElement elem)
			{
				string fileName = string.Empty;
				long offset = 0;
				long length = 0;

				for (XmlNode? child = elem.FirstChild; child != null; child = child.NextSibling)
				{
					if (child.Name == "Name")
					{
						fileName = child.InnerText;
					}
					else if (child.Name == "Offset")
					{
						long.TryParse(child.InnerText, out offset);
					}
					else if (child.Name == "Length")
					{
						long.TryParse(child.InnerText, out length);
					}
				}

				if (!string.IsNullOrEmpty(fileName))
				{
					bool isPkg = fileName.EndsWith(".package", StringComparison.OrdinalIgnoreCase);
					packagedFiles.Add(new PackagedFileInfo(fileName, offset, length, isPkg));
				}
			}
		}

		IsCorrupt = false;
		foreach (var item in packagedFiles)
		{
			if (item.IsPackage)
			{
				DBPFPackage? dBPFPackage = null;
				try
				{
					dBPFPackage = new DBPFPackage();
					dBPFPackage.Import(new SubStream(DataStore, dataStartPosition + item.Offset, item.Length), UseBuffer: false);
					dBPFPackage.GUID = Path.GetFileNameWithoutExtension(item.Name);
					if (dataStartPosition + item.Offset + item.Length > DataStore.Length)
					{
						IsCorrupt = true;
					}
				}
				catch (InvalidDataException)
				{
					dBPFPackage = null;
					IsEncrypted = true;
				}

				if (dBPFPackage != null)
				{
					Packages.Add(dBPFPackage);
				}
			}
			else
			{
				Thumbnails.Add(new SubStream(DataStore, dataStartPosition + item.Offset, item.Length));
			}
		}
	}

	public void Close()
	{
		Dispose();
	}

	public void Clear()
	{
		foreach (DBPFPackage package in Packages)
		{
			package.Close();
		}
		Packages.Clear();
		DataStore?.Close();
		DataStore = null;
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

	~Sims3Pack()
	{
		Dispose(disposing: false);
	}
}
