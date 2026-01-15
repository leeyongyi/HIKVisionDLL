using System;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net.NetworkInformation;

namespace HIKVisionDLL
{
    [Flags]
    public enum AnprResult
    {
        None = 0,
        Plate = 1,
        Container = 2
    }

    public class TcpServer
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private readonly int _port;
        private readonly string _type;
        private readonly CameraHandler _cameraHandler;

        public event Action<string, string, string, string> OnCaptureReceived;

        public TcpServer(int port, string type, CameraHandler cameraHandler)
        {
            _port = port;
            _type = type;
            _cameraHandler = cameraHandler;
        }

        public bool Start()
        {
            if (_listener != null) return true;

            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == _port))
            {
                RecordStatusLog($"Port {_port} is already in use. Server start aborted.");
                return false;
            }

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            Task.Run(() => ListenLoop(_cts.Token));
            return true;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client, token);
                }
                catch (ObjectDisposedException) 
                { 
                    break; 
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Server Accept Error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            string clientIp = "Unknown";
            try
            {
                if (client.Client != null && client.Client.RemoteEndPoint != null)
                {
                    clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                }
                
                RecordStatusLog($"[{_port}] Client connected: {clientIp}");
                
                client.ReceiveBufferSize = 1024 * 1024 * 10; // 10MB

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[65536];
                    int bytesRead;
                    List<byte> byteBuffer = new List<byte>();

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        RecordStatusLog($"[{_port}] Received {bytesRead} bytes from {clientIp}");
                        if (ProcessMessage(buffer, bytesRead, byteBuffer))
                        {
                            string response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                            RecordStatusLog($"[{_port}] Sent HTTP 200 OK");
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Client disconnected
            }
            catch (Exception ex)
            {
                RecordStatusLog($"[{_port}] Exception with client {clientIp}: {ex.Message}");
            }
            finally
            {
                RecordStatusLog($"[{_port}] Client disconnected: {clientIp}");
            }
        }

        private bool ProcessMessage(byte[] newBytes, int bytesRead, List<byte> byteBuffer)
        {
            byteBuffer.AddRange(newBytes.Take(bytesRead));

            if (byteBuffer.Count > 10 * 1024 * 1024) // 10MB
            {
                RecordStatusLog($"[{_port}] Buffer full ({byteBuffer.Count}), clearing...");
                byteBuffer.Clear();
                return false;
            }

            int headerSearchLength = Math.Min(byteBuffer.Count, 4096);
            string headerStr = Encoding.ASCII.GetString(byteBuffer.GetRange(0, headerSearchLength).ToArray());
            int headerEnd = headerStr.IndexOf("\r\n\r\n");

            if (headerEnd == -1) return false;

            string headers = headerStr.Substring(0, headerEnd);
            int contentLength = 0;
            Match match = Regex.Match(headers, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                contentLength = int.Parse(match.Groups[1].Value);
            }
            else
            {
                Match boundaryMatch = Regex.Match(headers, @"boundary=""?([^""\r\n;]+)""?", RegexOptions.IgnoreCase);
                if (boundaryMatch.Success)
                {
                    string boundary = boundaryMatch.Groups[1].Value;
                    string endBoundary = "--" + boundary + "--";
                    byte[] endBytes = Encoding.ASCII.GetBytes(endBoundary);
                    int endPos = ImageUtils.FindBytes(byteBuffer.ToArray(), endBytes, headerEnd + 4);
                    if (endPos != -1)
                    {
                        contentLength = (endPos + endBytes.Length) - (headerEnd + 4);
                    }
                }
            }

            int totalExpected = headerEnd + 4 + contentLength;
            if (byteBuffer.Count < totalExpected) return false;

            string body = Encoding.UTF8.GetString(byteBuffer.GetRange(headerEnd + 4, contentLength).ToArray());
            
            if (body.Length > 0)
                RecordStatusLog($"[{_port}] Content Preview: {body.Substring(0, Math.Min(body.Length, 200)).Replace("\r", "").Replace("\n", "")}...");

            string strPlate, strContainer;
            AnprResult result = ProcessAnprXml(body, out strPlate, out strContainer);

            string imagePath = null;
            string plateImagePath = null;

            if (result != AnprResult.None)
            {
                byte[] bodyBytes = byteBuffer.GetRange(headerEnd + 4, contentLength).ToArray();

                if (_type == "CNR")
                {
                    string filename = "detectionPicture_CNR.jpg";
                    if (ImageUtils.ExtractAndSaveImage(bodyBytes, "detectionPicture.jpg", filename))
                    {
                        imagePath = Path.Combine(@"C:\Data", filename);
                        RecordStatusLog($"[{_port}] Image saved to {imagePath}");
                    }
                }
                else
                {
                    string detectionFilename = "detectionPicture_LPR.jpg";
                    if (ImageUtils.ExtractAndSaveImage(bodyBytes, "detectionPicture.jpg", detectionFilename))
                    {
                        imagePath = Path.Combine(@"C:\Data", detectionFilename);
                        RecordStatusLog($"[{_port}] Detection image saved to {imagePath}");
                    }

                    string plateFilename = "licensePlatePicture_LPR.jpg";
                    
                    if (ImageUtils.ExtractAndSaveImageContaining(bodyBytes, "plate", plateFilename))
                    {
                        plateImagePath = Path.Combine(@"C:\Data", plateFilename);
                        RecordStatusLog($"[{_port}] Plate image saved to {plateImagePath}");
                    }
                    else
                    {
                        RecordStatusLog($"[{_port}] No plate image found in payload.");
                    }
                }
            }

            if ((result & AnprResult.Plate) != 0)
            {
                RecordStatusLog($"[{_port}] License Plate: {strPlate}");
                OnCaptureReceived?.Invoke(_type, strPlate, imagePath, plateImagePath);
            }

            if ((result & AnprResult.Container) != 0)
            {
                RecordStatusLog($"[{_port}] Container Full: {strContainer}");
                OnCaptureReceived?.Invoke(_type, strContainer, imagePath, plateImagePath);
            }

            byteBuffer.RemoveRange(0, totalExpected);
            return true;
        }

        private AnprResult ProcessAnprXml(string buffer, out string strPlate, out string strContainer)
        {
            strPlate = XmlUtils.ExtractValue(buffer, "licensePlate") ?? 
                       XmlUtils.ExtractValue(buffer, "plateNumber") ?? 
                       XmlUtils.ExtractValue(buffer, "originalLicensePlate") ??
                       XmlUtils.ExtractValue(buffer, "carCard");
            
            string containerMainNum = XmlUtils.ExtractValue(buffer, "containerMainNum");
            string containerSubNum = XmlUtils.ExtractValue(buffer, "containerSubNum");
            string containerISONum = XmlUtils.ExtractValue(buffer, "containerISONum");
            
            if (!string.IsNullOrEmpty(containerMainNum))
            {
                strContainer = containerMainNum + (containerSubNum ?? "") + (containerISONum ?? "");
            }
            else
            {
                strContainer = XmlUtils.ExtractValue(buffer, "containerNumber") ??
                               XmlUtils.ExtractValue(buffer, "containerNo") ??
                               XmlUtils.ExtractValue(buffer, "containerID") ??
                               XmlUtils.ExtractValue(buffer, "container") ??
                               XmlUtils.ExtractValue(buffer, "containerNum");
            }

            AnprResult result = AnprResult.None;
            if (!string.IsNullOrEmpty(strPlate)) result |= AnprResult.Plate;
            if (!string.IsNullOrEmpty(strContainer)) result |= AnprResult.Container;

            return result;
        }

        private void RecordStatusLog(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] {message}");
        }
    }
}
