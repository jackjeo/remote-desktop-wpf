using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace Controller
{
    public class ScreenDisplayService
    {
        private BitmapImage? _currentImage;

        public BitmapImage? ImageSource => _currentImage;

        public void UpdateFrame(byte[] jpegData)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                using var ms = new MemoryStream(jpegData);
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = ms;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                _currentImage = bitmapImage;
            }
            catch { }
        }

        public void ClearScreen()
        {
            _currentImage = null;
        }
    }
}
