using System.Net.Sockets;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using RFIDReaderPortal.Models;
namespace RFIDReaderPortal.Services
{
    public class TcpListenerService : ITcpListenerService
    {
       
        private TcpListener _tcpListener;
        private RfidData[] _receivedData;
        private string[] _hexString; 
        private int _dataCount;
        private int _hexdataCount;
        private readonly object _lock = new object();
        private string _accessToken;
        private string _userid;
        private string _recruitid;
        private string _deviceId;
        private string _location;
        private string _eventName;
        private string _sessionid;
        private string _ipaddress;
        private DateTime _lastClearTime = DateTime.MinValue;
        private readonly IApiService _apiService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiService> _logger;
        private Dictionary<string, DateTime> _processedTags = new Dictionary<string, DateTime>();
        private readonly TimeSpan _duplicatePreventionWindow = TimeSpan.FromSeconds(5);
        private int _lastSentIndex = 0;
        public bool IsRunning { get; private set; }
        private const int MAX_DATA_COUNT = 1000;
        private readonly List<RfidData> _storedRfidData = new List<RfidData>();
        public TcpListenerService( IApiService apiService, int port = 9090)
        {
           
            _apiService = apiService;
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _receivedData = new RfidData[MAX_DATA_COUNT];
            _hexString = new string[MAX_DATA_COUNT];
            _dataCount = 0;
            _hexdataCount = 0;
        }

        public void SetParameters(string accessToken, string userid, string recruitid, string deviceId, string location, string eventName, string ipaddress, string sesionid)
        {
            _accessToken = accessToken;
            _userid = userid;
            _recruitid = recruitid;
            _deviceId = deviceId;
            _location = location;
            _eventName = eventName;
            _sessionid = sesionid;
            _ipaddress = ipaddress;
            IsRunning = false;
        }

        
        public void Start()
        {
            if (!IsRunning)
            {
               _tcpListener.Start();
                IsRunning = true;
                Task.Run(async () => await ListenAsync());
            }
        }

        public void Stop()
        {
            if (IsRunning)
            {
                _tcpListener.Stop();
                IsRunning = false;
            }
        }

        private async Task ListenAsync()
        {
            while (IsRunning)
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                _ = Task.Run(() => ProcessClientAsync(client));
            }
        }

        private async Task ProcessClientAsync(TcpClient client)
        {
            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];
                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        var dataList = ParseRFIDDataMultiple(buffer, bytesRead);
                        if (dataList != null && dataList.Count > 0)
                        {
                            var currentTime = DateTime.Now;

                            foreach (var data in dataList)
                            {
                                bool shouldProcess = false;
                                RfidData existingData = null;

                                lock (_lock)
                                {
                                    existingData = _receivedData.Take(_dataCount)
                                        .FirstOrDefault(x => x?.TagId == data);

                                    if (existingData != null)
                                    {
                                        if ((currentTime - existingData.Timestamp) > _duplicatePreventionWindow)
                                        {
                                            existingData.Timestamp = currentTime;
                                            shouldProcess = true;
                                        }
                                    }
                                    else
                                    {
                                        shouldProcess = true;
                                    }
                                }

                                if (shouldProcess)
                                {
                                    if (existingData == null)
                                    {
                                        var rfidData = new RfidData
                                        {
                                            TagId = data,
                                            Timestamp = currentTime
                                        };

                                        lock (_lock)
                                        {
                                            if (_dataCount < MAX_DATA_COUNT)
                                            {
                                                _receivedData[_dataCount] = rfidData;
                                                _dataCount++;
                                            }
                                            else
                                            {
                                                Array.Copy(_receivedData, 1, _receivedData, 0, MAX_DATA_COUNT - 1);
                                                _receivedData[MAX_DATA_COUNT - 1] = rfidData;
                                            }
                                            Console.WriteLine($"Received new RFID data: {rfidData.TagId} at {rfidData.Timestamp:HH:mm:ss:fff}");
                                        }

                                        lock (_storedRfidData)
                                        {
                                            _storedRfidData.Add(rfidData); // Store in memory
                                        }
                                    }
                                    else
                                    {
                                        lock (_storedRfidData)
                                        {
                                            _storedRfidData.Add(existingData); // Store updated data
                                        }
                                        Console.WriteLine($"Updated existing RFID data: {existingData.TagId} at {existingData.Timestamp:HH:mm:ss:fff}");
                                    }

                                    _processedTags[data] = currentTime;
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task InsertStoredRfidDataAsync()
        {
            List<RfidData> dataToInsert;

            lock (_lock)
            {
                dataToInsert = new List<RfidData>(_storedRfidData);
                _storedRfidData.Clear(); // Clear after saving
            }

            if (dataToInsert.Count > 0)
            {
                await _apiService.PostRFIDRunningLogAsync(
                    _accessToken,
                    _userid,
                    _recruitid,
                    _deviceId,
                    _location,
                    _eventName,
                    dataToInsert,
                    _sessionid,
                    _ipaddress
                );
            }
        }

        private List<string> ParseRFIDDataMultiple(byte[] buffer, int bytesRead)
        {
            List<string> rfidTags = new List<string>();

            try
            {
                // Create a StringBuilder for the hex data
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytesRead; i++)
                {
                    sb.Append(buffer[i].ToString("X2")).Append(" ");
                }

                string hexstring = sb.ToString().Trim();

                // Process the hexstring in chunks of 128 characters (exactly 64 bytes)
                while (hexstring.Length >= 128)
                {
                    string currentSegment = hexstring.Substring(0, 128);
                    hexstring = hexstring.Substring(128).TrimStart();

                    // Check if the segment starts with "AA" to be considered valid
                    if (!currentSegment.StartsWith("AA"))
                    {
                        continue; // Skip this segment if it does not start with "AA"
                    }

                    string[] hexBytes = currentSegment.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (hexBytes.Length < 12)
                    {
                        continue;
                    }

                    for (int i = 29; i <= hexBytes.Length - 12; i += 12)
                    {
                        string joinedString = string.Join("", hexBytes.Skip(i).Take(12));

                        if (joinedString.Length == 24)
                        {
                            rfidTags.Add(joinedString);
                        }
                    }
                }

                lock (_lock)
                {
                    if (_hexdataCount < MAX_DATA_COUNT)
                    {
                        _hexString[_hexdataCount++] = sb.ToString().Trim();
                    }
                    else
                    {
                        // Shift entries in a circular buffer fashion
                        for (int i = 0; i < MAX_DATA_COUNT - 1; i++)
                        {
                            _hexString[i] = _hexString[i + 1];
                        }
                        _hexString[MAX_DATA_COUNT - 1] = sb.ToString().Trim();
                    }
                }

                return rfidTags;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing RFID data: {ex.Message}");
                return new List<string>();
            }
        }

        public void ClearData()
        {
            lock (_lock)
            {
                Array.Clear(_receivedData, 0, _receivedData.Length);
                _dataCount = 0;
                _processedTags.Clear();
                _lastClearTime = DateTime.Now;
            }
        }


        public RfidData[] GetReceivedData()
        {
            lock (_lock)
            {
                return _receivedData.Take(_dataCount)
                                    .Where(d => d.Timestamp > _lastClearTime)
                                    .ToArray();
            }
        }

        public string[] GetHexData()
        {
            lock (_lock)
            {
                return _hexString.Take(_hexdataCount)
                                    //.Where(d => d.Timestamp > _lastClearTime)
                                    .ToArray();
            }
        }

    }
}

