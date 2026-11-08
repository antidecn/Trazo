using System.Drawing;
using System.Drawing.Imaging;

namespace TrazoInstallerArt;

/// <summary>
/// Guarda imágenes como BMP de 8 bits con paleta, opcionalmente con compresión RLE8.
/// Las imágenes del asistente son casi todo superficies planas, así que con RLE8 ocupan muy poco
/// (en el .msi las imágenes van tal cual, sin la compresión del archivador de los archivos de la aplicación).
/// Windows Installer solo acepta RLE8 en los controles de imagen (Bitmap); los botones necesitan BMP sin comprimir.
/// </summary>
internal static class Bmp {

    public static void Save8(Bitmap source, string path, bool rle) {
        int w = source.Width, h = source.Height;
        int[] pixels = ReadPixels(source);

        // Paleta: los colores más frecuentes (los dibujos tienen pocos; el suavizado de bordes y texto añade algunos)
        var counts = new Dictionary<int, int>();
        foreach (int p in pixels) counts[p] = counts.TryGetValue(p, out int c) ? c + 1 : 1;
        int[] palette = counts.OrderByDescending(kv => kv.Value).Take(256).Select(kv => kv.Key).ToArray();
        var index = new Dictionary<int, byte>();
        for (int i = 0; i < palette.Length; i++) index[palette[i]] = (byte)i;

        byte Nearest(int argb) {
            if (index.TryGetValue(argb, out byte i)) return i;
            int best = 0, bestDistance = int.MaxValue;
            for (int k = 0; k < palette.Length; k++) {
                int dr = ((argb >> 16) & 255) - ((palette[k] >> 16) & 255);
                int dg = ((argb >> 8) & 255) - ((palette[k] >> 8) & 255);
                int db = (argb & 255) - (palette[k] & 255);
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            index[argb] = (byte)best;
            return (byte)best;
        }

        var data = new List<byte>(rle ? w * h / 4 : (w + 3) / 4 * 4 * h);
        if (rle) {
            // RLE8: por cada fila (de abajo arriba), pares (repeticiones, índice); 0 0 = fin de fila, 0 1 = fin de imagen
            for (int y = h - 1; y >= 0; y--) {
                int x = 0;
                while (x < w) {
                    byte v = Nearest(pixels[y * w + x]);
                    int run = 1;
                    while (x + run < w && run < 255 && Nearest(pixels[y * w + x + run]) == v) run++;
                    data.Add((byte)run); data.Add(v);
                    x += run;
                }
                data.Add(0); data.Add(0);
            }
            data[^1] = 1;
        } else {
            // Sin comprimir: un byte por píxel, filas de abajo arriba rellenas hasta múltiplo de 4
            int pad = (4 - w % 4) % 4;
            for (int y = h - 1; y >= 0; y--) {
                for (int x = 0; x < w; x++) data.Add(Nearest(pixels[y * w + x]));
                for (int i = 0; i < pad; i++) data.Add(0);
            }
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int colors = Math.Max(2, palette.Length);
        int paletteBytes = colors * 4;
        int offset = 14 + 40 + paletteBytes;
        // BITMAPFILEHEADER
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(offset + data.Count); bw.Write(0); bw.Write(offset);
        // BITMAPINFOHEADER (BI_RGB = 0, BI_RLE8 = 1)
        bw.Write(40); bw.Write(w); bw.Write(h);
        bw.Write((short)1); bw.Write((short)8);
        bw.Write(rle ? 1 : 0); bw.Write(data.Count);
        bw.Write(3780); bw.Write(3780);
        bw.Write(colors); bw.Write(0);
        for (int i = 0; i < colors; i++) {
            int c = i < palette.Length ? palette[i] : 0;
            bw.Write((byte)(c & 255)); bw.Write((byte)((c >> 8) & 255)); bw.Write((byte)((c >> 16) & 255)); bw.Write((byte)0);
        }
        bw.Write(data.ToArray());
    }

    private static int[] ReadPixels(Bitmap b) {
        var rect = new Rectangle(0, 0, b.Width, b.Height);
        BitmapData d = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try {
            var result = new int[b.Width * b.Height];
            for (int y = 0; y < b.Height; y++) {
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, result, y * b.Width, b.Width);
            }
            // Sin transparencia: todo se dibuja sobre un fondo opaco
            for (int i = 0; i < result.Length; i++) result[i] |= unchecked((int)0xFF000000);
            return result;
        } finally {
            b.UnlockBits(d);
        }
    }
}
