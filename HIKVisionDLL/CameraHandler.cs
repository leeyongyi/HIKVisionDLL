using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace HIKVisionDLL
{
    public class CameraHandler
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<(string result, string detectionPath, string platePath)> ManualCaptureAsync(string ip, string user, string pass, string isapi, string type)
        {
            return await RealCaptureAsync(ip, user, pass, isapi, type);
        }

        private async Task<(string result, string detectionPath, string platePath)> RealCaptureAsync(string ip, string user, string pass, string isapi, string type)
        {
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(user, pass),
                PreAuthenticate = true
            };

            using (var client = new HttpClient(handler))
            {
                string url = $"http://{ip}{isapi}";
                
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                byte[] data = await response.Content.ReadAsByteArrayAsync();

                string detectionFilename = type == "CNR" ? $"detectionPicture_CNR.jpg" : $"detectionPicture_LPR.jpg";
                string plateFilename = $"licensePlatePicture_LPR.jpg";

                bool detectionSaved = ImageUtils.ExtractAndSaveImage(data, "detectionPicture.jpg", detectionFilename);
                bool plateSaved = false;
                
                if (type == "LPR")
                {
                    plateSaved = ImageUtils.ExtractAndSaveImage(data, "licensePlatePicture.jpg", plateFilename);
                }

                
                string contentString = System.Text.Encoding.ASCII.GetString(data);
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] Manual Capture Response: {contentString}");

                string result = XmlUtils.ExtractValue(contentString, "licensePlate") ?? 
                                XmlUtils.ExtractValue(contentString, "plateNumber") ?? 
                                XmlUtils.ExtractValue(contentString, "originalLicensePlate");

                if (string.IsNullOrEmpty(result))
                {
                    string containerMainNum = XmlUtils.ExtractValue(contentString, "containerMainNum");
                    string containerSubNum = XmlUtils.ExtractValue(contentString, "containerSubNum");
                    
                    if (!string.IsNullOrEmpty(containerMainNum))
                    {
                        result = containerMainNum + (containerSubNum ?? "");
                    }
                    else
                    {
                        result = XmlUtils.ExtractValue(contentString, "containerNumber") ??
                                 XmlUtils.ExtractValue(contentString, "containerNo") ??
                                 XmlUtils.ExtractValue(contentString, "containerID") ??
                                 XmlUtils.ExtractValue(contentString, "container");
                    }
                }

                if (string.IsNullOrEmpty(result))
                {
                    result = "No Read";
                }

                return (result, detectionSaved ? Path.Combine(@"C:\Data", detectionFilename) : null, plateSaved ? Path.Combine(@"C:\Data", plateFilename) : null);
            }
        }



    }
}
