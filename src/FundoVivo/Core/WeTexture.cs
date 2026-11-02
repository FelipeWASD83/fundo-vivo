using System.Text;

namespace FundoVivo.Core;

/// <summary>One frame of an animated texture: which image of the sheet, how long it shows, and where it is.</summary>
public readonly record struct SpriteFrame(int Image, float Time, int X, int Y, int Width, int Height);

/// <summary>
/// A Wallpaper Engine .tex texture (layout in docs/pesquisa-cenas-wallpaper-engine.md, section 3.3): "TEXV0005",
/// "TEXI0001", seven int32 (format, flags, texture and image sizes, one unknown), "TEXB000n" with the images and
/// their mipmaps (raw or LZ4), and for animated ones "TEXS000n" with the frame table. Only uncompressed RGBA (format
/// 0) is decoded, which is what every animated texture in his library uses; only the first mipmap is kept.
/// </summary>
public sealed class WeTexture
{
    public int Format { get; private init; }
    public int Flags { get; private init; }
    public List<(int Width, int Height, byte[] Rgba)> Images { get; } = new();
    public List<SpriteFrame> Frames { get; } = new();

    public bool Animated => (Flags & 4) != 0 && Frames.Count > 0;
    /// <summary>Drawn without interpolation in Wallpaper Engine (pixel art, the "Point filter" of the GIF template).</summary>
    public bool PointFilter => (Flags & 1) != 0;

    /// <summary>Format and flags only, from the first bytes; enough to classify without reading the whole texture.</summary>
    public static (int Format, int Flags) Header(byte[] head)
    {
        int i = 0;
        SkipString(head, ref i);
        SkipString(head, ref i);
        return (BitConverter.ToInt32(head, i), BitConverter.ToInt32(head, i + 4));
    }

    public static WeTexture Read(byte[] b)
    {
        int i = 0;
        string v = ReadString(b, ref i), vi = ReadString(b, ref i);
        if (!v.StartsWith("TEXV", StringComparison.Ordinal) || !vi.StartsWith("TEXI", StringComparison.Ordinal)) throw new InvalidDataException("não é uma textura do Wallpaper Engine");
        int format = Int(b, ref i), flags = Int(b, ref i);
        i += 5 * 4;   // texture width and height, image width and height, unknown
        var tex = new WeTexture { Format = format, Flags = flags };
        string container = ReadString(b, ref i);
        int count = Int(b, ref i);
        int freeImage = -1, extra = 0;
        if (container is "TEXB0003" or "TEXB0004") freeImage = Int(b, ref i);
        if (container == "TEXB0004") extra = Int(b, ref i);
        if (format != 0 || freeImage != -1) throw new NotSupportedException($"textura no formato {format}/{freeImage}");
        for (int k = 0; k < count; k++)
        {
            int mips = Int(b, ref i);
            for (int m = 0; m < mips; m++)
            {
                if (container == "TEXB0004" && extra == 1) { i += 8; SkipString(b, ref i); i += 4; }
                int w = Int(b, ref i), h = Int(b, ref i), lz = 0, decoded = 0;
                if (container != "TEXB0001") { lz = Int(b, ref i); decoded = Int(b, ref i); }
                int size = Int(b, ref i);
                if (m == 0)
                {
                    byte[] rgba = lz == 1 ? Lz4.Decode(b, i, size, decoded) : b.AsSpan(i, size).ToArray();
                    if (rgba.Length < (long)w * h * 4) throw new InvalidDataException("imagem menor que o tamanho declarado");
                    tex.Images.Add((w, h, rgba));
                }
                i += size;
            }
        }
        if (i < b.Length)
        {
            string tag = ReadString(b, ref i);
            if (tag.StartsWith("TEXS", StringComparison.Ordinal))
            {
                int frames = Int(b, ref i);
                if (tag == "TEXS0003") i += 8;   // frame width and height, repeated in each frame below
                for (int n = 0; n < frames; n++, i += 32)
                {
                    int image = BitConverter.ToInt32(b, i);
                    float time = BitConverter.ToSingle(b, i + 4);
                    // TEXS0001 (the oldest GIF scenes) stores the rectangle as int32; the later ones as float
                    bool ints = tag == "TEXS0001";
                    float At(int offset) => ints ? BitConverter.ToInt32(b, i + offset) : BitConverter.ToSingle(b, i + offset);
                    float x = At(8), y = At(12), w1 = At(16), w2 = At(20), h2 = At(24), h1 = At(28);
                    // the two middle values turn the frame inside the sheet; none of his textures uses that
                    if (Math.Abs(w2) > 0.01f || Math.Abs(h2) > 0.01f) throw new NotSupportedException("quadro girado na folha");
                    tex.Frames.Add(new SpriteFrame(image, time, (int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(w1), (int)MathF.Round(h1)));
                }
            }
        }
        return tex;
    }

    /// <summary>Copies one frame out of its sheet, RGBA top to bottom.</summary>
    public void CopyFrame(SpriteFrame f, byte[] into)
    {
        var (w, h, rgba) = Images[f.Image];
        if (f.X < 0 || f.Y < 0 || f.X + f.Width > w || f.Y + f.Height > h) throw new InvalidDataException("quadro fora da folha");
        int row = f.Width * 4;
        for (int y = 0; y < f.Height; y++)
            Buffer.BlockCopy(rgba, ((f.Y + y) * w + f.X) * 4, into, y * row, row);
    }

    static int Int(byte[] b, ref int i) { int v = BitConverter.ToInt32(b, i); i += 4; return v; }

    static string ReadString(byte[] b, ref int i)
    {
        int end = Array.IndexOf(b, (byte)0, i);
        if (end < 0 || end - i > 64) throw new InvalidDataException("textura inválida");
        string s = Encoding.ASCII.GetString(b, i, end - i);
        i = end + 1;
        return s;
    }

    static void SkipString(byte[] b, ref int i) => ReadString(b, ref i);
}

/// <summary>The LZ4 block format (no frame header), which Wallpaper Engine uses inside its textures.</summary>
public static class Lz4
{
    public static byte[] Decode(byte[] src, int start, int length, int decodedSize)
    {
        var dst = new byte[decodedSize];
        int i = start, end = start + length, o = 0;
        while (i < end)
        {
            int token = src[i++];
            int literals = token >> 4;
            if (literals == 15) { int b; do { b = src[i++]; literals += b; } while (b == 255); }
            Buffer.BlockCopy(src, i, dst, o, literals);
            i += literals;
            o += literals;
            if (i >= end) break;   // the last sequence has literals only
            int offset = src[i] | (src[i + 1] << 8);
            i += 2;
            int match = token & 15;
            if (match == 15) { int b; do { b = src[i++]; match += b; } while (b == 255); }
            match += 4;
            int from = o - offset;
            if (from < 0) throw new InvalidDataException("LZ4 inválido");
            // overlapping copies repeat the bytes just written, so this goes byte by byte when they overlap
            if (offset >= match) { Buffer.BlockCopy(dst, from, dst, o, match); o += match; }
            else for (int k = 0; k < match; k++) dst[o++] = dst[from + k];
        }
        if (o != decodedSize) throw new InvalidDataException("LZ4 com tamanho diferente do declarado");
        return dst;
    }
}
