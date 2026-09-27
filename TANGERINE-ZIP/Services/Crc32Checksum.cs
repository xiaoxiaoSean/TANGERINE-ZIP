namespace TANGERINE_ZIP.Services;

internal static class Crc32Checksum
{
    private static readonly uint[] Table = BuildTable();

    public static uint Update(uint state, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) state = Table[(state ^ value) & 0xff] ^ (state >> 8);
        return state;
    }

    public static bool Matches(long expected, uint state) =>
        expected <= 0 || expected > uint.MaxValue || (uint)expected == ~state;

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
