using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Controller
{
    public class FileTransferService
    {
        public async Task SendFileAsync(string filePath, NetworkStream stream, Action<int> progressCallback)
        {
            string filename = Path.GetFileName(filePath);
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            byte[] headerBytes = Encoding.UTF8.GetBytes($"FILE_RECV:{filename}:{fileSize}:");
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[8192];
            long totalSent = 0;
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await stream.WriteAsync(buffer, 0, bytesRead);
                totalSent += bytesRead;

                int progress = (int)(totalSent * 100 / fileSize);
                progressCallback(progress);
            }
        }

        public async Task<byte[]?> ReceiveFileAsync(NetworkStream stream, long fileSize)
        {
            byte[] buffer = new byte[8192];
            using var ms = new MemoryStream();

            long totalReceived = 0;
            while (totalReceived < fileSize)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, fileSize - totalReceived);
                int bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead);
                if (bytesRead == 0) return null;

                await ms.WriteAsync(buffer, 0, bytesRead);
                totalReceived += bytesRead;
            }

            return ms.ToArray();
        }
    }
}
