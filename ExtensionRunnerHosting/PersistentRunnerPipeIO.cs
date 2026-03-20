using System;
using System.IO;
using System.Text;

namespace Popokey.ExtensionRunners
{
    internal static class PersistentRunnerPipeIO
    {
        private const int MaxMessageBytes = 256 * 1024 * 1024;

        internal static void WriteMessage<T>(Stream stream, T message)
        {
            string json = PersistentRunnerJson.Serialize(message);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);

            stream.Write(lengthPrefix, 0, lengthPrefix.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        internal static T ReadMessage<T>(Stream stream)
        {
            byte[] lengthPrefix = ReadExact(stream, sizeof(int));
            int messageLength = BitConverter.ToInt32(lengthPrefix, 0);
            if (messageLength < 0 || messageLength > MaxMessageBytes)
            {
                throw new InvalidOperationException($"Pipe message length '{messageLength}' is invalid.");
            }

            byte[] payload = ReadExact(stream, messageLength);
            string json = Encoding.UTF8.GetString(payload);
            return PersistentRunnerJson.Deserialize<T>(json);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int bytesRead = stream.Read(buffer, offset, count - offset);
                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException("The pipe closed before the full message was received.");
                }

                offset += bytesRead;
            }

            return buffer;
        }
    }
}
