using System;
using System.IO;
using System.Runtime.CompilerServices;
using Tiger;

namespace S3ForgeTools.GameFiles.Package;

public static class Compression
{
	public static byte[] UncompressStream(Stream stream, int filesize, int memsize)
	{
		byte[] output = GC.AllocateUninitializedArray<byte>(memsize);

		// If stream can provide buffer or small enough, read into span
		byte[] inputBuffer = new byte[filesize];
		stream.ReadExactly(inputBuffer, 0, filesize);

		if (!Decompress(inputBuffer, output))
		{
			throw new InvalidDataException("Decompression Failure");
		}

		return output;
	}

	public static bool Decompress(ReadOnlySpan<byte> input, Span<byte> output)
	{
		if (input.Length < 2) return false;

		byte b0 = input[0];
		byte b1 = input[1];

		// RefPack signature check (0xFB in byte 1, or EA signature)
		if (b1 != 0xFB && (b0 & 0x3E) != 0x10)
		{
			// Try without header or validate
		}

		int headerLen = (((b0 & 0x80) != 0) ? 4 : 3) * (((b0 & 1) == 0) ? 1 : 2) + 2;
		if (input.Length < headerLen) return false;

		int inPos = headerLen;
		int outPos = 0;
		int inputLen = input.Length;
		int outputLen = output.Length;

		while (inPos < inputLen && outPos < outputLen)
		{
			byte cmd = input[inPos++];

			int numLit = 0;
			int numMatch = 0;
			int offset = 0;

			if (cmd < 0x80)
			{
				// 2-byte command (0x00 .. 0x7F)
				if (inPos >= inputLen) break;
				byte b = input[inPos++];
				numLit = cmd & 0x03;
				numMatch = ((cmd >> 2) & 0x07) + 3;
				offset = (((cmd << 3) & 0x300) | b) + 1;
			}
			else if (cmd < 0xC0)
			{
				// 3-byte command (0x80 .. 0xBF)
				if (inPos + 1 >= inputLen) break;
				byte b = input[inPos++];
				byte c = input[inPos++];
				numLit = (b >> 6) & 0x03;
				numMatch = (cmd & 0x3F) + 4;
				offset = (((b << 8) & 0x3F00) | c) + 1;
			}
			else if (cmd < 0xE0)
			{
				// 4-byte command (0xC0 .. 0xDF)
				if (inPos + 2 >= inputLen) break;
				byte b = input[inPos++];
				byte c = input[inPos++];
				byte d = input[inPos++];
				numLit = cmd & 0x03;
				numMatch = (((cmd << 6) & 0x300) | d) + 5;
				offset = (((cmd << 12) & 0x10000) | (b << 8) | c) + 1;
			}
			else if (cmd < 0xFC)
			{
				// Literal run command (0xE0 .. 0xFB)
				numLit = ((cmd & 0x1F) + 1) << 2;
			}
			else
			{
				// Stop command / trailing literals (0xFC .. 0xFF)
				numLit = cmd & 0x03;
			}

			// Copy literals
			if (numLit > 0)
			{
				if (inPos + numLit > inputLen || outPos + numLit > outputLen)
				{
					return false;
				}

				input.Slice(inPos, numLit).CopyTo(output.Slice(outPos, numLit));
				inPos += numLit;
				outPos += numLit;
			}

			// Copy match back-reference
			if (numMatch > 0)
			{
				if (offset > outPos || outPos + numMatch > outputLen)
				{
					return false;
				}

				int matchSrc = outPos - offset;
				if (offset >= numMatch)
				{
					output.Slice(matchSrc, numMatch).CopyTo(output.Slice(outPos, numMatch));
					outPos += numMatch;
				}
				else
				{
					// Overlapping match: copy byte-by-byte or run
					for (int i = 0; i < numMatch; i++)
					{
						output[outPos++] = output[matchSrc + i];
					}
				}
			}

			if (cmd >= 0xFC)
			{
				break;
			}
		}

		return outPos == outputLen;
	}

	public static byte[] CompressStream(byte[] data, int level = 1)
	{
		return Tiger.DBPFCompression.Compress(data, out byte[] compressed, level) ? compressed : data;
	}
}
