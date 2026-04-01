using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;

namespace Agent
{
    public class CommandExecutor
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public void MoveMouse(int x, int y)
        {
            SetCursorPos(x, y);
        }

        public void MouseDown(int x, int y)
        {
            SetCursorPos(x, y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
        }

        public void MouseUp(int x, int y)
        {
            SetCursorPos(x, y);
            mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
        }

        public void KeyPress(int vkCode)
        {
            keybd_event((byte)vkCode, 0, 0, 0);
            keybd_event((byte)vkCode, 0, KEYEVENTF_KEYUP, 0);
        }

        public void TypeText(string text)
        {
            foreach (char c in text)
            {
                short vkScan = VkKeyScan(c);
                byte vkCode = (byte)(vkScan & 0xFF);
                keybd_event(vkCode, 0, 0, 0);
                keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
            }
        }

        public void ReceiveFile(string filename, long fileSize, NetworkStream stream)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = Path.Combine(desktopPath, filename);

            int totalReceived = 0;
            byte[] buffer = new byte[8192];

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            while (totalReceived < fileSize)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, fileSize - totalReceived);
                int bytesRead = stream.Read(buffer, 0, bytesToRead);
                if (bytesRead == 0) break;

                fs.Write(buffer, 0, bytesRead);
                totalReceived += bytesRead;
            }
        }
    }
}
