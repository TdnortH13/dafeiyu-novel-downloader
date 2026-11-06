using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class MakeIcon
{
    static int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: MakeIcon <in.png> <out.ico>");
            return 2;
        }
        string inPath = args[0];
        string outPath = args[1];

        Bitmap src;
        using (FileStream fs = new FileStream(inPath, FileMode.Open, FileAccess.Read))
        using (Image img = Image.FromStream(fs))
        {
            src = new Bitmap(img);
        }
        Console.WriteLine("source: " + src.Width + "x" + src.Height);

        MemoryStream[] payloads = new MemoryStream[Sizes.Length];
        for (int i = 0; i < Sizes.Length; i++)
        {
            int s = Sizes[i];
            using (Bitmap scaled = Resize(src, s))
            {
                if (s >= 128)
                    payloads[i] = EncodePng(scaled);
                else
                    payloads[i] = EncodeDib(scaled);
            }
            Console.WriteLine("  " + s + "x" + s + " -> " + payloads[i].Length + " bytes (" + (s >= 128 ? "png" : "dib") + ")");
        }

        using (FileStream outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(outFs))
        {
            w.Write((ushort)0);              // reserved
            w.Write((ushort)1);              // type = icon
            w.Write((ushort)Sizes.Length);   // count

            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                int s = Sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));  // width
                w.Write((byte)(s >= 256 ? 0 : s));  // height
                w.Write((byte)0);                   // colors
                w.Write((byte)0);                   // reserved
                w.Write((ushort)1);                 // planes
                w.Write((ushort)32);                // bpp
                w.Write((uint)payloads[i].Length);
                w.Write((uint)offset);
                offset += (int)payloads[i].Length;
            }
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write(payloads[i].ToArray());
            }
        }
        Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
        return 0;
    }

    static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, size, size),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
        }
        return dst;
    }

    static MemoryStream EncodePng(Bitmap bmp)
    {
        MemoryStream ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }

    static MemoryStream EncodeDib(Bitmap bmp)
    {
        int s = bmp.Width;
        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);

        // BITMAPINFOHEADER
        w.Write((uint)40);
        w.Write((int)s);
        w.Write((int)(s * 2));
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write((uint)0);
        w.Write((uint)(s * s * 4));
        w.Write((int)0);
        w.Write((int)0);
        w.Write((uint)0);
        w.Write((uint)0);

        // XOR (BGRA, bottom-up)
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bd.Stride;
            byte[] row = new byte[stride];
            for (int y = s - 1; y >= 0; y--)
            {
                IntPtr p = new IntPtr(bd.Scan0.ToInt64() + (long)y * stride);
                System.Runtime.InteropServices.Marshal.Copy(p, row, 0, stride);
                w.Write(row, 0, s * 4);
            }
        }
        finally { bmp.UnlockBits(bd); }

        // AND mask (1bpp, bottom-up, rows padded to 4 bytes)
        int maskRow = ((s + 31) / 32) * 4;
        byte[] zero = new byte[maskRow];
        for (int y = 0; y < s; y++) w.Write(zero, 0, maskRow);

        w.Flush();
        ms.Position = 0;
        return ms;
    }
}
