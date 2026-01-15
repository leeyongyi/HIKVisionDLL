using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace HIKVisionDLL
{
    public static class ImageUtils
    {
        private const string DataPath = @"C:\Data";

        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(DataPath))
            {
                Directory.CreateDirectory(DataPath);
            }
        }

        public static string SaveImageBytes(byte[] imageBytes, string filename)
        {
            EnsureDirectoryExists();
            string fullPath = Path.Combine(DataPath, filename);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            File.WriteAllBytes(fullPath, imageBytes);
            return fullPath;
        }

        public static bool ExtractAndSaveImage(byte[] data, string searchFilename, string saveFilename)
        {
            byte[] searchBytes = System.Text.Encoding.ASCII.GetBytes($"filename=\"{searchFilename}\"");
            int nameIndex = FindBytes(data, searchBytes);

            if (nameIndex != -1)
            {
                int headerEnd = FindBytes(data, new byte[] { 13, 10, 13, 10 }, nameIndex);
                if (headerEnd != -1)
                {
                    int startPos = headerEnd + 4;

                    int endPos = FindBytes(data, new byte[] { 13, 10, 45, 45 }, startPos);

                    if (endPos != -1)
                    {
                        int length = endPos - startPos;
                        byte[] imageBytes = new byte[length];
                        Array.Copy(data, startPos, imageBytes, 0, length);

                        SaveImageBytes(imageBytes, saveFilename);
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool ExtractAndSaveImageContaining(byte[] data, string nameContains, string saveFilename)
        {
            byte[] filenamePrefix = System.Text.Encoding.ASCII.GetBytes("filename=\"");
            int currentIndex = 0;

            while (true)
            {
                int nameIndex = FindBytes(data, filenamePrefix, currentIndex);
                if (nameIndex == -1) break;

                int valueStart = nameIndex + filenamePrefix.Length;
                int valueEnd = -1;
                for (int i = valueStart; i < data.Length; i++)
                {
                    if (data[i] == (byte)'"')
                    {
                        valueEnd = i;
                        break;
                    }
                }

                if (valueEnd != -1)
                {
                    string foundName = System.Text.Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart);
                    if (foundName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int headerEnd = FindBytes(data, new byte[] { 13, 10, 13, 10 }, valueEnd);
                        if (headerEnd != -1)
                        {
                            int startPos = headerEnd + 4;
                            int endPos = FindBytes(data, new byte[] { 13, 10, 45, 45 }, startPos);

                            if (endPos != -1)
                            {
                                int length = endPos - startPos;
                                byte[] imageBytes = new byte[length];
                                Array.Copy(data, startPos, imageBytes, 0, length);

                                SaveImageBytes(imageBytes, saveFilename);
                                return true;
                            }
                        }
                    }
                    currentIndex = valueEnd + 1;
                }
                else
                {
                    break;
                }
            }
            return false;
        }

        public static int FindBytes(byte[] haystack, byte[] needle, int startIndex = 0)
        {
            for (int i = startIndex; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
