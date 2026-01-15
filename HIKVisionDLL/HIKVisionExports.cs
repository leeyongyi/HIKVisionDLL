using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HIKVisionDLL
{
    public static class HIKVisionExports
    {
        private static Dictionary<int, TcpServer> _servers = new Dictionary<int, TcpServer>();
        private static Dictionary<int, string> _portResults = new Dictionary<int, string>();
        private static CameraHandler _cameraHandler = new CameraHandler();

        // Default values
        private const string DEFAULT_IP_LPR = "192.168.1.65";
        private const string DEFAULT_IP_CNR = "192.168.1.64";
        private const string DEFAULT_USERNAME = "admin";
        private const string DEFAULT_PASSWORD = "abcd2468";
        private const string DEFAULT_ISAPI = "/ISAPI/Traffic/MNPR/channels/1";

        [UnmanagedCallersOnly(EntryPoint = "StartTcpServer")]
        public static unsafe int StartTcpServer(int port, byte* typePtr)
        {
            try
            {
                string type = Marshal.PtrToStringUTF8((IntPtr)typePtr) ?? "LPR";
                
                if (_servers.ContainsKey(port))
                {
                    return 1; // Already running
                }

                // Check if port is already in use
                if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners().Any(e => e.Port == port))
                {
                    return 0; // Port in use
                }

                var server = new TcpServer(port, type, _cameraHandler);
                server.OnCaptureReceived += (t, result, img, plate) =>
                {
                    _portResults[port] = result;
                };

                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        server.Start();
                    }
                    catch { }
                });
                thread.IsBackground = true;
                thread.Start();

                _servers[port] = server;
                _portResults[port] = "";
                return 1; // Success
            }
            catch
            {
                return 0;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "GetLastResult")]
        public static unsafe int GetLastResult(int port, byte* buffer, int bufferSize)
        {
            try
            {
                if (!_portResults.ContainsKey(port) || string.IsNullOrEmpty(_portResults[port]))
                    return 0;

                string result = _portResults[port];
                
                _portResults[port] = "";

                byte[] resultBytes = Encoding.UTF8.GetBytes(result);
                int copyLength = Math.Min(resultBytes.Length, bufferSize - 1);
                
                Marshal.Copy(resultBytes, 0, (IntPtr)buffer, copyLength);
                buffer[copyLength] = 0; // Null terminator
                
                return copyLength;
            }
            catch
            {
                return 0;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "ProcessData")]
        public static unsafe int ProcessData(byte* ipPtr, byte* usernamePtr, byte* passwordPtr, byte* isapiPtr, byte* typePtr, byte* resultBuffer, int resultBufferSize)
        {
            try
            {
                string ip = Marshal.PtrToStringUTF8((IntPtr)ipPtr);
                string username = Marshal.PtrToStringUTF8((IntPtr)usernamePtr);
                string password = Marshal.PtrToStringUTF8((IntPtr)passwordPtr);
                string isapi = Marshal.PtrToStringUTF8((IntPtr)isapiPtr);
                string type = Marshal.PtrToStringUTF8((IntPtr)typePtr);

                // Apply defaults if empty
                if (string.IsNullOrEmpty(type)) type = "LPR";
                if (string.IsNullOrEmpty(ip)) ip = (type.Equals("LPR", StringComparison.OrdinalIgnoreCase)) ? DEFAULT_IP_LPR : DEFAULT_IP_CNR;
                if (string.IsNullOrEmpty(username)) username = DEFAULT_USERNAME;
                if (string.IsNullOrEmpty(password)) password = DEFAULT_PASSWORD;
                if (string.IsNullOrEmpty(isapi)) isapi = DEFAULT_ISAPI;

                var task = _cameraHandler.ManualCaptureAsync(ip, username, password, isapi, type);
                task.Wait();
                var result = task.Result;

                byte[] resultBytes = Encoding.UTF8.GetBytes(result.result);
                int copyLength = Math.Min(resultBytes.Length, resultBufferSize - 1);
                
                Marshal.Copy(resultBytes, 0, (IntPtr)resultBuffer, copyLength);
                resultBuffer[copyLength] = 0; // Null terminator

                return copyLength;
            }
            catch
            {
                return 0;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "StopTcpServer")]
        public static int StopTcpServer(int port)
        {
            try
            {
                if (_servers.ContainsKey(port))
                {
                    _servers[port].Stop();
                    _servers.Remove(port);
                    _portResults.Remove(port);
                    return 1;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
