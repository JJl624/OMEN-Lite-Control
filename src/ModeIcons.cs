using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OmenLiteControl
{
    // One vector design rendered at native icon sizes; no runtime assets or icon library.
    internal sealed class ModeIcons : IDisposable
    {
        internal static readonly Color[] Colors = { Color.FromArgb(45, 181, 119),
                                                    Color.FromArgb(235, 70, 70),
                                                    Color.FromArgb(46, 151, 230),
                                                    Color.FromArgb(128, 140, 156) };
        readonly Icon[] icons = new Icon[4];

        internal ModeIcons()
        {
            for (int i = 0; i < icons.Length; i++)
                using (var stream = new MemoryStream(CreateData(Colors[i]))) using (
                    var source = new Icon(stream)) icons[i] = (Icon)source.Clone();
        }

        internal Icon ForMode(int mode)
        {
            return icons[mode >= 0 && mode <= 2 ? mode : 3];
        }

        internal static byte[] CreateData(Color color)
        {
            int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (
                    var bitmap = new Bitmap(
                        sizes[i], sizes[i],
                        PixelFormat
                            .Format32bppArgb)) using (var g =
                                                          Graphics.FromImage(
                                                              bitmap)) using (var stream =
                                                                                  new MemoryStream())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(sizes[i] / 32f, sizes[i] / 32f);
                    using (var background = new SolidBrush(Color.FromArgb(21, 28, 40))) using (
                        var path = new GraphicsPath())
                    {
                        path.AddArc(1, 1, 10, 10, 180, 90);
                        path.AddArc(21, 1, 10, 10, 270, 90);
                        path.AddArc(21, 21, 10, 10, 0, 90);
                        path.AddArc(1, 21, 10, 10, 90, 90);
                        path.CloseFigure();
                        g.FillPath(background, path);
                    }
                    using (var pen = new Pen(color, 3.5f) { LineJoin = LineJoin.Round })
                        g.DrawPolygon(pen, new[] { new PointF(16, 5), new PointF(27, 16),
                                                   new PointF(16, 27), new PointF(5, 16) });
                    using (var pen = new Pen(Color.White, 2.5f) { StartCap = LineCap.Round,
                                                                  EndCap = LineCap.Round })
                        g.DrawLine(pen, 16, 12, 16, 20);
                    // DIB frames also work with the .NET Framework icon loader at every size.
                    int size = sizes[i], maskStride = ((size + 31) / 32) * 4;
                    using (var frame = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    {
                        frame.Write(40);
                        frame.Write(size);
                        frame.Write(size * 2);
                        frame.Write((ushort)1);
                        frame.Write((ushort)32);
                        frame.Write(0);
                        frame.Write(size * size * 4 + maskStride * size);
                        frame.Write(0);
                        frame.Write(0);
                        frame.Write(0);
                        frame.Write(0);
                        for (int y = size - 1; y >= 0; y--)
                            for (int x = 0; x < size; x++)
                            {
                                Color pixel = bitmap.GetPixel(x, y);
                                frame.Write(pixel.B);
                                frame.Write(pixel.G);
                                frame.Write(pixel.R);
                                frame.Write(pixel.A);
                            }
                        for (int y = size - 1; y >= 0; y--)
                        {
                            var mask = new byte[maskStride];
                            for (int x = 0; x < size; x++)
                                if (bitmap.GetPixel(x, y).A == 0)
                                    mask[x / 8] |= (byte)(0x80 >> (x % 8));
                            frame.Write(mask);
                        }
                    }
                    frames[i] = stream.ToArray();
                }
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var frame in frames)
                    writer.Write(frame);
                return stream.ToArray();
            }
        }

        public void Dispose()
        {
            foreach (var icon in icons)
                if (icon != null)
                    icon.Dispose();
        }
    }
}
