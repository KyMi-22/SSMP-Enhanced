using System;
using System.IO;

// Read-only validation. PNG pixels and the author's relative asset names are never changed.
public static class SkinAssetChecks
{
    static readonly uint[] CrcTable = BuildTable();
    static uint[] BuildTable()
    {
        var result = new uint[256];
        for (uint n = 0; n < result.Length; n++)
        {
            uint c = n;
            for (int bit = 0; bit < 8; bit++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            result[n] = c;
        }
        return result;
    }
    static uint UInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
    static void ReadExactly(Stream input, byte[] data, int length)
    {
        int at = 0;
        while (at < length)
        {
            int count = input.Read(data, at, length - at);
            if (count == 0) throw new InvalidDataException("Truncated image");
            at += count;
        }
    }
    public static string VerifyPng(string path)
    {
        using (var input = File.OpenRead(path))
        {
            var header = new byte[8];
            ReadExactly(input, header, 8);
            byte[] expected = {137, 80, 78, 71, 13, 10, 26, 10};
            for (int i = 0; i < 8; i++) if (header[i] != expected[i]) throw new InvalidDataException("Invalid PNG signature: " + path);
            var buffer = new byte[65536];
            var storedCrc = new byte[4];
            int chunks = 0;
            bool dataSeen = false;
            uint width = 0, height = 0;
            while (input.Position < input.Length)
            {
                ReadExactly(input, header, 8);
                uint size = UInt32(header, 0);
                if (size > int.MaxValue || input.Length - input.Position < (long)size + 4) throw new InvalidDataException("Invalid PNG chunk length: " + path);
                string kind = System.Text.Encoding.ASCII.GetString(header, 4, 4);
                if ((chunks == 0 && kind != "IHDR") || (kind == "IHDR" && (chunks != 0 || size != 13))) throw new InvalidDataException("Invalid PNG IHDR: " + path);
                uint crc = 0xffffffffu;
                for (int i = 4; i < 8; i++) crc = CrcTable[(crc ^ header[i]) & 255] ^ (crc >> 8);
                uint remaining = size;
                bool firstBlock = true;
                while (remaining > 0)
                {
                    int count = (int)Math.Min((uint)buffer.Length, remaining);
                    ReadExactly(input, buffer, count);
                    if (kind == "IHDR" && firstBlock)
                    {
                        width = UInt32(buffer, 0); height = UInt32(buffer, 4);
                        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue || buffer[10] != 0 || buffer[11] != 0 || buffer[12] > 1)
                            throw new InvalidDataException("Invalid PNG dimensions/encoding: " + path);
                    }
                    for (int i = 0; i < count; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                    remaining -= (uint)count;
                    firstBlock = false;
                }
                ReadExactly(input, storedCrc, 4);
                if ((crc ^ 0xffffffffu) != UInt32(storedCrc, 0)) throw new InvalidDataException("PNG CRC mismatch: " + path + " (" + kind + ")");
                chunks++;
                if (kind == "IDAT" && size > 0) dataSeen = true;
                if (kind == "IEND")
                {
                    if (size != 0 || !dataSeen || input.Position != input.Length) throw new InvalidDataException("Invalid PNG end: " + path);
                    return width + "x" + height;
                }
            }
            throw new InvalidDataException("Missing PNG IEND: " + path);
        }
    }
    public static void VerifyWebmHeader(string path)
    {
        using (var input = File.OpenRead(path))
        {
            var header = new byte[4];
            ReadExactly(input, header, 4);
            if (UInt32(header, 0) != 0x1a45dfa3u || input.Length <= 4) throw new InvalidDataException("Invalid WebM EBML header: " + path);
        }
    }
}
