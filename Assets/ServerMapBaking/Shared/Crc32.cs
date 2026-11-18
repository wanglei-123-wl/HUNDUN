namespace ServerMapBaking
{
    public static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            for (int index = 0; index < bytes.Length; index++)
            {
                byte tableIndex = (byte)(crc ^ bytes[index]);
                crc = Table[tableIndex] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFF;
        }

        private static uint[] CreateTable()
        {
            uint[] table = new uint[256];
            for (int index = 0; index < table.Length; index++)
            {
                uint value = (uint)index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
                }

                table[index] = value;
            }

            return table;
        }
    }
}
