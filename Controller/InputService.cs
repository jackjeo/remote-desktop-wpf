namespace Controller
{
    public class InputService
    {
        private int _screenWidth;
        private int _screenHeight;

        public void SetScreenSize(int width, int height)
        {
            _screenWidth = width;
            _screenHeight = height;
        }

        public (int x, int y) ScaleToRemote(int localX, int localY, int localWidth, int localHeight)
        {
            if (_screenWidth == 0 || _screenHeight == 0 || localWidth == 0 || localHeight == 0)
                return (localX, localY);

            int remoteX = (int)((double)localX / localWidth * _screenWidth);
            int remoteY = (int)((double)localY / localHeight * _screenHeight);

            return (remoteX, remoteY);
        }
    }
}
