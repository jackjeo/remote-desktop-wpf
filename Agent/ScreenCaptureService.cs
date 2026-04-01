using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Agent
{
    public class ScreenCaptureService
    {
        public byte[] CaptureScreen()
        {
            int width = (int)System.Windows.SystemParameters.VirtualScreenWidth;
            int height = (int)System.Windows.SystemParameters.VirtualScreenHeight;
            int left = (int)System.Windows.SystemParameters.VirtualScreenLeft;
            int top = (int)System.Windows.SystemParameters.VirtualScreenTop;

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));

            using var ms = new MemoryStream();
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            bitmap.Save(ms, jpegEncoder, encoderParams);
            return ms.ToArray();
        }

        public (int width, int height) GetScreenSize()
        {
            int width = (int)System.Windows.SystemParameters.VirtualScreenWidth;
            int height = (int)System.Windows.SystemParameters.VirtualScreenHeight;
            return (width, height);
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return codecs[0];
        }
    }
}
