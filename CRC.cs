namespace PKSprojekt;

using System;

class CRC
{
    private static readonly ushort[] table = new ushort[256];
    private const ushort polynomial = 0xA001; // Reversed polynomial (0x8005)

    static CRC()
    {
        // Inicializácia tabuľky CRC-16
        for (ushort i = 0; i < table.Length; i++)
        {
            ushort value = i;
            for (byte j = 0; j < 8; j++)
            {
                if ((value & 1) != 0)
                {
                    value = (ushort)((value >> 1) ^ polynomial);
                }
                else
                {
                    value >>= 1;
                }
            }
            table[i] = value;
        }
    }

    public static ushort ComputeChecksum(byte[] data)
    {
        ushort crc = 0xFFFF; // Počiatočná hodnota CRC-16

        foreach (byte b in data)
        {
            byte tableIndex = (byte)(crc ^ b);
            crc = (ushort)((crc >> 8) ^ table[tableIndex]);
        }

        return crc;
    }

    public static bool VerifyChecksum(byte[] data, ushort expectedCrc)
    {
        ushort calculatedCrc = ComputeChecksum(data);
        return calculatedCrc == expectedCrc;
    }
}