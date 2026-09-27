using System;
using System.Globalization;
using System.Runtime.CompilerServices;

#nullable enable
namespace S3ForgeTools.GameFiles.Package;

public readonly record struct TGI_Key(uint Type, uint Group, ulong Instance) : IComparable<TGI_Key>, IComparable<string>, IEquatable<TGI_Key>
{
	public static readonly TGI_Key Empty = default;

	public TGI_Key(string Keyvalue)
		: this(Keyvalue.AsSpan())
	{
	}

	public TGI_Key(ReadOnlySpan<char> keySpan)
		: this(0, 0, 0)
	{
		if (TryParse(keySpan, out TGI_Key parsed))
		{
			this = parsed;
		}
	}

	public static bool TryParse(ReadOnlySpan<char> s, out TGI_Key key)
	{
		key = default;
		if (s.IsEmpty) return false;

		if (s.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
		{
			s = s.Slice(4);
		}

		// Expected format: TTTTTTTT-GGGGGGGG-IIIIIIIIIIIIIIII or TTTTTTTT-GGGGGGGG-IIIIIIII-IIIIIIII
		if (s.Length >= 34 && s[8] == '-' && s[17] == '-')
		{
			ReadOnlySpan<char> typeSpan = s.Slice(0, 8);
			ReadOnlySpan<char> groupSpan = s.Slice(9, 8);
			ReadOnlySpan<char> instSpan = s.Slice(18);

			if (uint.TryParse(typeSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint type) &&
			    uint.TryParse(groupSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint group))
			{
				if (instSpan.Length == 16 && ulong.TryParse(instSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong instance))
				{
					key = new TGI_Key(type, group, instance);
					return true;
				}
				else if (instSpan.Length >= 17 && instSpan[8] == '-')
				{
					ReadOnlySpan<char> hiSpan = instSpan.Slice(0, 8);
					ReadOnlySpan<char> loSpan = instSpan.Slice(9);
					if (ulong.TryParse(hiSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hi) &&
					    ulong.TryParse(loSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lo))
					{
						key = new TGI_Key(type, group, (hi << 32) | lo);
						return true;
					}
				}
			}
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override int GetHashCode()
	{
		return HashCode.Combine(Type, Group, Instance);
	}

	public override string ToString()
	{
		return $"{Type:x8}-{Group:x8}-{Instance:x16}";
	}

	public int CompareTo(string? other)
	{
		return string.Compare(ToString(), other, StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CompareTo(TGI_Key other)
	{
		int num = Type.CompareTo(other.Type);
		if (num != 0) return num;

		num = Group.CompareTo(other.Group);
		if (num != 0) return num;

		return Instance.CompareTo(other.Instance);
	}
}
