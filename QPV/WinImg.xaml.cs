using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QPV {
  class ImageMatcher {

    public struct Box {
      public int minx;
      public int maxx;
      public int miny;
      public int maxy;
    }

    public class Mask {
      private readonly bool[] mask;
      private readonly int W;
      private readonly int H;

      public Mask(int w, int h) {
        mask = new bool[w * h];
        W = w;
        H = h;
        for (int i = 0; i < w * h; i++)
          mask[i] = false;
      }

      public bool V(int x, int y) {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        return mask[x + W * y];
      }
      public void V(int x, int y, bool v) {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        mask[x + W * y] = v;
      }

      internal void RemoveIsolated() {
        for (int x = 1; x < W - 1; x++) {
          if (mask[x] && !mask[x - 1] && !mask[x + 1] && !mask[x - 1 + W] && !mask[x + W] && !mask[x + 1 + W]) mask[x] = false;
          if (mask[x + W * (H - 1)] && !mask[x - 1 + W * (H - 1)] && !mask[x + 1 + W * (H - 1)] && !mask[x - 1 + W * (H - 1)] && !mask[x + W * (H - 1)] && !mask[x + 1 + W * (H - 1)]) mask[x] = false;
        }

        for (int y = 1; y < H - 1; y++) {
          if (mask[W * y] && !mask[W * (y - 1)] && !mask[W * (y + 1)] && !mask[1 + W * y] && !mask[1 + W * (y - 1)] && !mask[1 + W * (y + 1)]) mask[W * y] = false;
          if (mask[W - 1 + W * y] && !mask[W - 1 + W * (y - 1)] && !mask[W - 1 + W * (y + 1)] && !mask[W - 2 + W * y] && !mask[W - 2 + W * (y - 1)] && !mask[W - 2 + W * (y + 1)]) mask[W * y] = false;
        }

        for (int y = 1; y < H - 1; y++) {
          for (int x = 1; x < W - 1; x++) {
            if (mask[x + W * y]) {
              if (!mask[x - 1 + W * (y - 1)] && !mask[x + W * (y - 1)] && !mask[x + 1 + W * (y - 1)] && !mask[x - 1 + W * (y)] && !mask[x + 1 + W * (y)] && !mask[x - 1 + W * (y + 1)] && !mask[x + W * (y + 1)] && !mask[x + 1 + W * (y + 1)])
                mask[x + W * y] = false;
            }
          }
        }
      }

      internal Box GetRect(Box b) {
        for (int y = 0; y < H; y++)
          for (int x = 0; x < W; x++)
            if (mask[x + W * y]) {
              if (b.minx > x) b.minx = x;
              if (b.maxx < x) b.maxx = x;
              if (b.miny > y) b.miny = y;
              if (b.maxy < y) b.maxy = y;
            }
        return b;
      }
    }


    public struct Pixel {
      public byte R, G, B;
      public Pixel(byte r, byte g, byte b) {
        R = r;
        G = g;
        B = b;
      }

      internal static double Distance(Pixel p0, Pixel p1) {
        return Math.Sqrt((p0.R - p1.R) * (p0.R - p1.R) + (p0.G - p1.G) * (p0.G - p1.G) + (p0.B - p1.B) * (p0.B - p1.B)) / 443.405;
      }
    }



    struct PixelsNormalized {
      private readonly int w;
      private readonly int h;
      private readonly byte[] data;

      public PixelsNormalized(byte[] src, int sw, int sh, System.Drawing.Imaging.PixelFormat pf, int bytesPerPixel) {
        w = sw;
        h = sh;
        data = new byte[3 * h * w];
        for (int y = 0; y < h; y++)
          for (int x = 0; x < w; x++) {
            int posD = (x + w * y) * 3;
            int posS = (x + w * y) * bytesPerPixel;
            if (pf == System.Drawing.Imaging.PixelFormat.Format24bppRgb) {
              data[posD + 0] = src[posS + 0]; // B
              data[posD + 1] = src[posS + 1]; // G
              data[posD + 2] = src[posS + 2]; // R
            }
            else if (pf == System.Drawing.Imaging.PixelFormat.Format32bppArgb) {
              data[posD + 0] = src[posS + 2]; // B
              data[posD + 1] = src[posS + 1]; // G
              data[posD + 2] = src[posS + 0]; // R
            }
          }
      }

      public PixelsNormalized(int sw, int sh) {
        w = sw;
        h = sh;
        data = new byte[3 * h * w];
      }

      public Pixel Get(int x, int y) {
        if (x <0 || y < 0 || x >= w || y >= h)
          return new Pixel(255, 0, 0);
        int pos = (x + w * y) * 3;
        return new Pixel(data[pos + 2], data[pos + 1], data[pos]);
      }

      public void Set(int x, int y, Pixel p) {
        if (x <0 || y < 0 || x >= w || y >= h) return;
        int pos = (x + w * y) * 3;
        data[pos + 2] = p.R;
        data[pos + 1] = p.G;
        data[pos] = p.B;
      }

      public Pixel Average(int x, int y) {
        if (x < 0 || y < 0 || x > w - 2 || y > h - 2) return new Pixel(0, 0, 0);
        int pos00 = (x + w * y) * 3;
        int pos01 = (x + 1 + w * y) * 3;
        int pos10 = (x + w * (y + 1)) * 3;
        int pos11 = (x + 1 + w * (y + 1)) * 3;
        int r = (data[pos00 + 2] + data[pos01 + 2] + data[pos10 + 2] + data[pos11 + 2]) / 4;
        int g = (data[pos00 + 1] + data[pos01 + 1] + data[pos10 + 1] + data[pos11 + 1]) / 4;
        int b = (data[pos00 + 0] + data[pos01 + 0] + data[pos10 + 0] + data[pos11 + 0]) / 4;
        return new Pixel((byte)r, (byte)g, (byte)b);
      }

      internal byte GetAverageLuma(int x, int y, int blockXsize, int blockYsize) {
        double luma = 0.0;
        int done = 0;
        for (int i = 0; i < blockXsize; i++)
          for (int j = 0; j < blockYsize; j++) {
            int px = x * blockXsize + i;
            int py = y * blockYsize + j;
            if (px >= w || py >= h)
              continue;
            int pos = (px + w * py) * 3;
            luma += (0.299 * data[pos + 2] + 0.587 * data[pos + 1] + 0.114 * data[pos + 0]);
            done++;
          }
        if (done == 0) return 0;
        return (byte)(luma / done);
      }
      internal byte GetAverageHue(int x, int y, int blockXsize, int blockYsize) {
        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        for (int i = 0; i < blockXsize; i++)
          for (int j = 0; j < blockYsize; j++) {
            int px = x * blockXsize + i;
            int py = y * blockYsize + j;
            if (px >= w || py >= h) continue;
            int pos = (px + w * py) * 3;
            r += data[pos + 2];
            g += data[pos + 1];
            b += data[pos + 0];
          }

        // Convert to HSL
        Colors.HSL hsl = Colors.Colors.RGBtoHSL((byte)(r / (blockXsize * blockYsize)), (byte)(g / (blockXsize * blockYsize)), (byte)(b / (blockXsize * blockYsize)));
        if (hsl.Saturation < 0.075 || hsl.Luminance < 0.025)
          return 0;
        return (byte)(1 + (254.0 * hsl.Hue / 360.0));
      }
    }

    public static void CalculateHash(ImageInfo image, string path, double blurDiff) {
      // Load the image
      Bitmap srcbmp;
      try {
        FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        srcbmp = new Bitmap(fs);
        fs.Close();
      } catch (Exception ex) {
        image._hashL = image._hashC = null;
        Trace.WriteLine(ex.Message);
        return;
      }

      // Get the image bytes
      int h = srcbmp.Height;
      int w = srcbmp.Width;
      Rectangle rect = new Rectangle(0, 0, w, h);
      System.Drawing.Imaging.BitmapData bmpData = srcbmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, srcbmp.PixelFormat);
      int numBytes = Math.Abs(bmpData.Stride) * srcbmp.Height;
      byte[] rgbValues = new byte[numBytes];
      Marshal.Copy(bmpData.Scan0, rgbValues, 0, numBytes);
      int bytesPerPixel = bmpData.Stride / srcbmp.Width;

      PixelsNormalized pixs = new PixelsNormalized(rgbValues, w, h, srcbmp.PixelFormat, bytesPerPixel);

      // Run the blur
      Mask mask = new Mask(w, h);
      for (int y = 0; y < h - 1; y++) { // Not the last pixels, or they will be always bad
        for (int x = 0; x < w - 1; x++) {
          Pixel pixel0 = pixs.Get(x, y);
          Pixel pixel1 = pixs.Average(x, y);
          if (Pixel.Distance(pixel0, pixel1) > blurDiff)
            mask.V(x, y, true);
        }
      }
      // Now eliminate the points that are isolated
      mask.RemoveIsolated();
      // Find the min and max in horiz and vert
      Box box;
      box.minx = w;
      box.miny = h;
      box.maxx = 0;
      box.maxy = 0;
      box = mask.GetRect(box);

      // Then create the new image from the rectangle found
      w = box.maxx - box.minx + 1;
      h = box.maxy - box.miny + 1;
      PixelsNormalized img = new PixelsNormalized(w, h);
      for (int y = box.miny; y <= box.maxy; y++)
        for (int x = box.minx; x <= box.maxx; x++) {
          Pixel p = pixs.Get(x, y);
          img.Set(x - box.minx, y - box.miny, p);
        }


      // Now calculate the hash as before
      int blockXsize = (w + 15) / 16;
      int blockYsize = (h + 15) / 16;

      if (image._hashL == null) image._hashL = new byte[256];
      if (image._hashC == null) image._hashC = new byte[256];
      for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++) {
          byte luma = img.GetAverageLuma(x, y, blockXsize, blockYsize);
          byte hue = img.GetAverageHue(x, y, blockXsize, blockYsize);
          image._hashL[x + y * 16] = luma;
          image._hashC[x + y * 16] = hue;
        }

      srcbmp.Dispose();
    }
  }
}


namespace QPV2 {
  public partial class WinImg : Window {
    public double blurDiff = 0.5;

    public WinImg() {
      InitializeComponent();

      // Load a base pic in the left image
      Uri oUri = new Uri("pack://application:,,,/TestImgs/Dalitso_3.jpg", UriKind.RelativeOrAbsolute);
      ILeft.Source = BitmapFrame.Create(oUri);
    }


    string imgpath = @"C:\Users\claud\Source\Repos\QuickPictureView\QPV\TestImgs\Dalitso_3.jpg";

    private void OnLoad(object sender, RoutedEventArgs e) {
      CommonOpenFileDialog d = new CommonOpenFileDialog();
      if (d.ShowDialog() != CommonFileDialogResult.Ok)
        return;
      imgpath = d.FileName;
      BitmapFrame bmf = BitmapFrame.Create(new Uri(imgpath, UriKind.RelativeOrAbsolute));
      ILeft.Source = bmf;
      IRight.Source = bmf;
    }

    private void OnExecute(object sender, RoutedEventArgs e) {
      PBar.Visibility = Visibility.Visible;
      PBar.Value = 0;
      Stopwatch watch = Stopwatch.StartNew();


      FileStream fs = File.Open(imgpath, FileMode.Open, FileAccess.Read, FileShare.Read);
      Bitmap srcbmp = new System.Drawing.Bitmap(fs);
      fs.Close();

      ILeft.Source = BitmapFrame.Create(new Uri(imgpath, UriKind.RelativeOrAbsolute));

      int h = srcbmp.Height;
      int w = srcbmp.Width;
      PBar.Maximum = h;


      Rectangle rect = new Rectangle(0, 0, w, h);
      System.Drawing.Imaging.BitmapData bmpData = srcbmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, srcbmp.PixelFormat);
      int numBytes = Math.Abs(bmpData.Stride) * srcbmp.Height;
      byte[] rgbValues = new byte[numBytes];
      Marshal.Copy(bmpData.Scan0, rgbValues, 0, numBytes);
      int bytesPerPixel = bmpData.Stride / srcbmp.Width;

      // Transform, depending on pixelformat, the rgb values
      byte[] srcRGB = new byte[3 * h * w];

      for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) {
          int posD = (x + w * y) * bytesPerPixel;
          int posS = (x + w * y) * 3;
          if (srcbmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb) {
            srcRGB[posD + 0] = rgbValues[posS + 0]; // B
            srcRGB[posD + 1] = rgbValues[posS + 1]; // G
            srcRGB[posD + 2] = rgbValues[posS + 2]; // R
          }
          else if (srcbmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb) {
            srcRGB[posD + 0] = rgbValues[posS + 2]; // B
            srcRGB[posD + 1] = rgbValues[posS + 1]; // G
            srcRGB[posD + 2] = rgbValues[posS + 0]; // R
          }
        }


      // Run the blur
      int avgs = 0;
      RGBM blur = new RGBM(w, h, srcRGB);
      Mask mask = new Mask(w, h);
      RGB check = new RGB(255, 0, 0);
      for (int y = 0; y < h - 1; y++) { // Not the last pixels, or they will be always bad
        for (int x = 0; x < w - 1; x++) {
          RGB pixel00 = blur.Get(x, y);
          RGB pixel01 = blur.Get(x, y + 1);
          RGB pixel10 = blur.Get(x + 1, y);
          RGB pixel11 = blur.Get(x + 1, y + 1);

          RGB average = new RGB(pixel00, pixel01, pixel10, pixel11);
          if (RGB.Distance(pixel00, average) < blurDiff) {
            // Blur
            blur.Set(x, y, average);
            avgs++;
          }
          else
            mask.V(x, y, true);
        }
        PBar.Dispatcher.Invoke(new Action(() => { PBar.Value = y; }), DispatcherPriority.ContextIdle);
      }
      // Now eliminate the points that are isolated
      mask.RemoveIsolated();
      // Find the min and max in horiz and vert
      Box box;
      box.minx = w;
      box.miny = h;
      box.maxx = 0;
      box.maxy = 0;
      box = mask.GetRect(box);
      // Then create the new image from the rectangle found
      Bitmap dstbmp = new Bitmap(box.maxx - box.minx + 1, box.maxy - box.miny + 1);
      PBar.Value = 0;
      PBar.Maximum = dstbmp.Width * dstbmp.Height;
      // And increase the luminosity of all highlighted pixels, while decreasing luma of other pixels
      for (int y = box.miny; y <= box.maxy; y++)
        for (int x = box.minx; x <= box.maxx; x++) {
          RGB rgb = blur.Get(x, y);
          if (mask.V(x, y)) {
            rgb.R = 255;
            rgb.G = rgb.B = 10;
          }
          dstbmp.SetPixel(x-box.minx, y-box.miny, rgb.GetColor());
        }

      watch.Stop();
      long elapsedMs = watch.ElapsedMilliseconds;
      int secs = (int)elapsedMs / 1000 % 60;
      int mins = ((int)elapsedMs / 1000) / 60 % 60;
      int hours = ((int)elapsedMs / 3600000);

      Title = "Avgs: " + avgs + " (" + (100 * avgs / (w * h)) + "%) " + hours + ":" + (mins < 10 ? "0" : "") + mins + ":" + (secs < 10 ? "0" : "") + secs;
      PBar.Visibility = Visibility.Hidden;

      // Set it as image
      using (MemoryStream memory = new MemoryStream()) {
        dstbmp.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        IRight.Source = bitmapImage;
      }
    }

    private void OnSliders(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (sender == BlurDiff) {
        blurDiff = BlurDiff.Value;
        LBlurDiff.Content = "Max distance: " + blurDiff;
      }
    }
  }

  public struct Box {
    public int minx;
    public int maxx;
    public int miny;
    public int maxy;
  }

  public class Mask {
    public bool[] _Mask;
    private readonly int W;
    private readonly int H;

    public Mask(int w, int h) {
      _Mask = new bool[w * h];
      W = w;
      H = h;
      for (int i = 0; i < w * h; i++)
        _Mask[i] = false;
    }

    public bool V(int x, int y) {
      if (x < 0 || y < 0 || x >= W || y >= H) return false;
      return _Mask[x + W * y];
    }
    public void V(int x, int y, bool v) {
      if (x < 0 || y < 0 || x >= W || y >= H) return;
      _Mask[x + W * y] = v;
    }

    internal void RemoveIsolated() {
      for(int x=1; x<W-1; x++) {
        if (_Mask[x] && !_Mask[x - 1] && !_Mask[x + 1] && !_Mask[x - 1 + W] && !_Mask[x + W] && !_Mask[x + 1 + W]) _Mask[x] = false;
        if (_Mask[x + W*(H-1)] && !_Mask[x - 1 + W * (H - 1)] && !_Mask[x + 1 + W * (H - 1)] && !_Mask[x - 1 + W * (H - 1)] && !_Mask[x + W * (H - 1)] && !_Mask[x + 1 + W * (H - 1)]) _Mask[x] = false;
      }

      for(int y=1; y<H-1; y++) {
        if (_Mask[W*y] && !_Mask[W * (y - 1)] && !_Mask[W * (y + 1)] && !_Mask[1 + W * y] && !_Mask[1 + W * (y - 1)] && !_Mask[1 + W * (y + 1)]) _Mask[W*y] = false;
        if (_Mask[W-1+W * y] && !_Mask[W-1+W * (y - 1)] && !_Mask[W-1+W * (y + 1)] && !_Mask[W-2 + W * y] && !_Mask[W-2 + W * (y - 1)] && !_Mask[W-2 + W * (y + 1)]) _Mask[W * y] = false;
      }

      for (int y = 1; y < H - 1; y++) {
        for (int x = 1; x < W - 1; x++) {
          if (_Mask[x + W * y]) {
            if (!_Mask[x - 1 + W * (y - 1)] && !_Mask[x + W * (y - 1)] && !_Mask[x + 1 + W * (y - 1)] && !_Mask[x - 1 + W * (y)] && !_Mask[x + 1 + W * (y)] && !_Mask[x - 1 + W * (y + 1)] && !_Mask[x + W * (y + 1)] && !_Mask[x + 1 + W * (y + 1)])
              _Mask[x + W * y] = false;
          }
        }
      }
    }

    internal Box GetRect(Box b) {
      for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
          if (_Mask[x + W * y]) {
            if (b.minx > x) b.minx = x;
            if (b.maxx < x) b.maxx = x;
            if (b.miny > y) b.miny = y;
            if (b.maxy < y) b.maxy = y;
          }
      return b;
    }
  }

  public class RGB {
    public byte R, G, B;
    public RGB(RGB p0, RGB p1, RGB p2, RGB p3) {
      R = (byte)((p0.R + p1.R + p2.R + p3.R) / 4);
      G = (byte)((p0.G + p1.G + p2.G + p3.G) / 4);
      B = (byte)((p0.B + p1.B + p2.B + p3.B) / 4);
    }
    public RGB(byte r, byte g, byte b) {
      R = r;
      G = g;
      B = b;
    }

    internal static double Distance(RGB p0, RGB p1) {
      return Math.Sqrt((p0.R - p1.R) * (p0.R - p1.R) + (p0.G - p1.G) * (p0.G - p1.G) + (p0.B - p1.B) * (p0.B - p1.B)) / 443.405;
    }

    internal void Lighten() {
      Colors.HSL hsl = Colors.Colors.RGBtoHSL(R, G, B);
      hsl.Luminance = hsl.Luminance * 2.0;
      Colors.RGB rgb = Colors.Colors.HSLtoRGB(hsl.Hue, hsl.Saturation, hsl.Luminance);
      R = (byte)rgb.Red;
      G = (byte)rgb.Green;
      B = (byte)rgb.Blue;
    }

    internal void Darken() {
      Colors.HSL hsl = Colors.Colors.RGBtoHSL(R, G, B);
      hsl.Luminance = hsl.Luminance / 2.0;
      Colors.RGB rgb = Colors.Colors.HSLtoRGB(hsl.Hue, hsl.Saturation, hsl.Luminance);
      R = (byte)rgb.Red;
      G = (byte)rgb.Green;
      B = (byte)rgb.Blue;
    }

    internal Color GetColor() {
      return Color.FromArgb(255, R, G, B);
    }
  }

  public class RGBM {
    byte[] rgb;
    int width;
    int height;

    public RGBM(int w, int h, byte[] src) {
      width = w;
      height = h;
      rgb = new byte[w * h * 3];
      for (int i = 0; i < w * h * 3; i++)
        rgb[i] = src[i];
    }

    public RGB Get(int x, int y) {
      if (x >= width || x < 0 || y < 0 || y >= height) return new RGB(0, 0, 0);
      return new RGB(R(x, y), G(x, y), B(x, y));
    }

    public byte R(int x, int y) {
      return rgb[(x + width * y) * 3 + 2];
    }
    public byte G(int x, int y) {
      return rgb[(x + width * y) * 3 + 1];
    }
    public byte B(int x, int y) {
      return rgb[(x + width * y) * 3 + 0];
    }
    public void R(int x, int y, byte r) {
      rgb[(x + width * y) * 3 + 2] = r;
    }
    public void G(int x, int y, byte g) {
      rgb[(x + width * y) * 3 + 1] = g;
    }
    public void B(int x, int y, byte b) {
      rgb[(x + width * y) * 3 + 0] = b;
    }

    internal void Set(int x, int y, RGB p) {
      rgb[(x + width * y) * 3 + 0] = p.B;
      rgb[(x + width * y) * 3 + 1] = p.G;
      rgb[(x + width * y) * 3 + 2] = p.R;
    }
  }
}
