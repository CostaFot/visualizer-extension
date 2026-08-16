using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Storage.Streams;

namespace VisualizerExtension;

// Pre-baked dot icons for the VU dock band: one solid anti-aliased disc PNG per VuPalette step,
// delivered as STREAM-backed IconData (an in-memory IRandomAccessStreamReference per step) —
// NOT as data:image/png;base64 URI strings. String icons funnel into the host's
// IconPathConverter, which feeds the URI to BitmapImage.UriSource, and WinUI 3 does not support
// the data: scheme there — the image fails asynchronously and renders as nothing (found the hard
// way 2026-08-16; evidence in notes/rendering.md § "Color channels"). The stream branch of the
// host's IconLoaderService decodes the bytes directly and verifiably renders. The IconData must
// keep its Icon STRING empty: a non-empty string wins over Data in the host loader.
//
// All StepCount icons (~2 KB each) are baked once at first touch; the band only ever swaps
// cached IconInfo instances. The PNG writer below is deliberately minimal — RGBA8, one zlib
// "stored" deflate block, no compression: ~60 lines beats an image library dependency for
// 16 tiny dots.
internal static class VuDotIcons
{
    private const int Size = 32; // drawn at 16 DIP in the dock -> 2x for HiDPI

    private static readonly IconInfo[] _icons = BuildIcons();

    public static IconInfo Icon(int step) => _icons[Math.Clamp(step, 0, _icons.Length - 1)];

    private static IconInfo[] BuildIcons()
    {
        var icons = new IconInfo[VuPalette.StepCount];
        for (var step = 0; step < icons.Length; step++)
        {
            var (r, g, b) = VuPalette.Rgb(step);
            icons[step] = new IconInfo(new IconData(CreateStreamReference(EncodeDotPng(r, g, b))));
        }

        return icons;
    }

    // Each step keeps its own live stream reference: the host re-opens it on every icon load, and
    // its icon cache keys stream icons by reference identity — distinct references per color can
    // never collide into one cached bitmap.
    private static RandomAccessStreamReference CreateStreamReference(byte[] png)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            // Completes on the thread pool (in-memory, no UI affinity) — safe to block once here.
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }

        return RandomAccessStreamReference.CreateFromStream(stream);
    }

    // A centered disc on transparent ground, edge softened over one pixel.
    private static byte[] EncodeDotPng(byte r, byte g, byte b)
    {
        const float center = (Size - 1) / 2f;
        const float radius = Size * 0.42f;

        // PNG scanlines: one filter byte (0 = None) then RGBA per pixel.
        var raw = new byte[Size * (1 + (Size * 4))];
        var i = 0;
        for (var y = 0; y < Size; y++)
        {
            raw[i++] = 0;
            for (var x = 0; x < Size; x++)
            {
                var d = MathF.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
                var alpha = Math.Clamp(radius + 0.5f - d, 0f, 1f);
                raw[i++] = r;
                raw[i++] = g;
                raw[i++] = b;
                raw[i++] = (byte)((alpha * 255f) + 0.5f);
            }
        }

        return WritePng(raw);
    }

    private static byte[] WritePng(byte[] raw)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        WriteUInt32BE(ihdr, 0, Size);
        WriteUInt32BE(ihdr, 4, Size);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // color type: RGBA
        WriteChunk(png, "IHDR", ihdr);

        // zlib stream: 2-byte header, ONE final stored deflate block (raw fits 16-bit length at
        // Size 32: 4,128 bytes), Adler-32 of the uncompressed bytes.
        var idat = new byte[2 + 5 + raw.Length + 4];
        idat[0] = 0x78;
        idat[1] = 0x01;
        idat[2] = 0x01; // BFINAL=1, BTYPE=00 (stored)
        idat[3] = (byte)(raw.Length & 0xFF);
        idat[4] = (byte)(raw.Length >> 8);
        idat[5] = (byte)~idat[3];
        idat[6] = (byte)~idat[4];
        raw.CopyTo(idat, 7);
        WriteUInt32BE(idat, idat.Length - 4, Adler32(raw));
        WriteChunk(png, "IDAT", idat);

        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        var header = new byte[8];
        WriteUInt32BE(header, 0, (uint)data.Length);
        for (var i = 0; i < 4; i++)
        {
            header[4 + i] = (byte)type[i];
        }

        png.Write(header);
        png.Write(data);

        var crc = Crc32(header.AsSpan(4, 4), data);
        var footer = new byte[4];
        WriteUInt32BE(footer, 0, crc);
        png.Write(footer);
    }

    private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> head, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, head);
        crc = Crc32Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)(-(int)(crc & 1)));
            }
        }

        return crc;
    }
}
