//using System.Net.Sockets;
//using System.Net;
//using System.Text;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.ApplicationModels;
//using Microsoft.Extensions.Configuration;
//using System.Net.Http;
//using System.Threading.Tasks;
//using RFIDReaderPortal.Models;
//namespace RFIDReaderPortal.Services
//{
//    public class TcpListenerService : ITcpListenerService
//    {

//        private TcpListener _tcpListener;
//        private RfidData[] _receivedData;
//        private string[] _hexString; 
//        private int _dataCount;
//        private int _hexdataCount;
//        private readonly object _lock = new object();
//        private string _accessToken;
//        private string _userid;
//        private string _recruitid;
//        private string _deviceId;
//        private string _location;
//        private string _eventName;
//        private string _sessionid;
//        private string _ipaddress;
//        private DateTime _lastClearTime = DateTime.MinValue;
//        private readonly IApiService _apiService;
//        private readonly IConfiguration _configuration;
//        private readonly ILogger<ApiService> _logger;
//        private Dictionary<string, DateTime> _processedTags = new Dictionary<string, DateTime>();
//        private readonly TimeSpan _duplicatePreventionWindow = TimeSpan.FromSeconds(5);
//        private int _lastSentIndex = 0;
//        public bool IsRunning { get; private set; }
//        private const int MAX_DATA_COUNT = 1000;
//        private readonly List<RfidData> _storedRfidData = new List<RfidData>();
//        public TcpListenerService( IApiService apiService, int port = 9090)
//        {

//            _apiService = apiService;
//            _tcpListener = new TcpListener(IPAddress.Any, port);
//            _receivedData = new RfidData[MAX_DATA_COUNT];
//            _hexString = new string[MAX_DATA_COUNT];
//            _dataCount = 0;
//            _hexdataCount = 0;
//        }

//        public void SetParameters(string accessToken, string userid, string recruitid, string deviceId, string location, string eventName, string ipaddress, string sesionid)
//        {
//            _accessToken = accessToken;
//            _userid = userid;
//            _recruitid = recruitid;
//            _deviceId = deviceId;
//            _location = location;
//            _eventName = eventName;
//            _sessionid = sesionid;
//            _ipaddress = ipaddress;
//            IsRunning = false;
//        }


//        public void Start()
//        {
//            if (!IsRunning)
//            {
//               _tcpListener.Start();
//                IsRunning = true;
//                Task.Run(async () => await ListenAsync());
//            }
//        }

//        public void Stop()
//        {
//            if (IsRunning)
//            {
//                _tcpListener.Stop();
//                IsRunning = false;
//            }
//        }

//        private async Task ListenAsync()
//        {
//            while (IsRunning)
//            {
//                var client = await _tcpListener.AcceptTcpClientAsync();
//                _ = Task.Run(() => ProcessClientAsync(client));
//            }
//        }

//        private async Task ProcessClientAsync(TcpClient client)
//        {
//            using (var stream = client.GetStream())
//            {
//                var buffer = new byte[1024];
//                while (client.Connected)
//                {
//                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
//                    if (bytesRead > 0)
//                    {
//                        var dataList = ParseRFIDDataMultiple(buffer, bytesRead);
//                        if (dataList != null && dataList.Count > 0)
//                        {
//                            var currentTime = DateTime.Now;

//                            foreach (var data in dataList)
//                            {
//                                bool shouldProcess = false;
//                                RfidData existingData = null;

//                                lock (_lock)
//                                {
//                                    existingData = _receivedData.Take(_dataCount)
//                                        .FirstOrDefault(x => x?.TagId == data);

//                                    if (existingData != null)
//                                    {
//                                        if ((currentTime - existingData.Timestamp) > _duplicatePreventionWindow)
//                                        {
//                                            existingData.Timestamp = currentTime;
//                                            shouldProcess = true;
//                                        }
//                                    }
//                                    else
//                                    {
//                                        shouldProcess = true;
//                                    }
//                                }

//                                if (shouldProcess)
//                                {
//                                    if (existingData == null)
//                                    {
//                                        var rfidData = new RfidData
//                                        {
//                                            TagId = data,
//                                            Timestamp = currentTime
//                                        };

//                                        lock (_lock)
//                                        {
//                                            if (_dataCount < MAX_DATA_COUNT)
//                                            {
//                                                _receivedData[_dataCount] = rfidData;
//                                                _dataCount++;
//                                            }
//                                            else
//                                            {
//                                                Array.Copy(_receivedData, 1, _receivedData, 0, MAX_DATA_COUNT - 1);
//                                                _receivedData[MAX_DATA_COUNT - 1] = rfidData;
//                                            }
//                                            Console.WriteLine($"Received new RFID data: {rfidData.TagId} at {rfidData.Timestamp:HH:mm:ss:fff}");
//                                        }

//                                    lock (_storedRfidData)
//                                    {
//                                        _storedRfidData.Add(rfidData); // Store in memory
//                                    }
//                                    //lock (_storedRfidData)
//                                    //{
//                                    //    int limit = GetMaxLimitByEvent();

//                                    //    // Remove duplicates for same tag with same timestamp
//                                    //    _storedRfidData.Add(rfidData);

//                                    //    var filtered = _storedRfidData
//                                    //        .Where(x => x.TagId == rfidData.TagId)
//                                    //        .OrderByDescending(x => x.Timestamp)
//                                    //        .Take(limit)
//                                    //        .ToList();

//                                    //    _storedRfidData.RemoveAll(x => x.TagId == rfidData.TagId);
//                                    //    _storedRfidData.AddRange(filtered);
//                                    //}

//                                }
//                                    else
//                                    {
//                                    //lock (_storedRfidData)
//                                    //{
//                                    //    int limit = GetMaxLimitByEvent();

//                                    //    _storedRfidData.Add(existingData);

//                                    //    var filtered = _storedRfidData
//                                    //        .Where(x => x.TagId == existingData.TagId)
//                                    //        .OrderByDescending(x => x.Timestamp)
//                                    //        .Take(limit)
//                                    //        .ToList();

//                                    //    _storedRfidData.RemoveAll(x => x.TagId == existingData.TagId);
//                                    //    _storedRfidData.AddRange(filtered);

//                                    //}

//                                    lock (_storedRfidData)
//                                    {
//                                        _storedRfidData.Add(existingData); // Store updated data
//                                    }
//                                    Console.WriteLine($"Updated existing RFID data: {existingData.TagId} at {existingData.Timestamp:HH:mm:ss:fff}");
//                                }

//                                _processedTags[data] = currentTime;
//                                    }
//                                }
//                            }
//                        }
//                    }
//                }
//            }




//        public async Task InsertStoredRfidDataAsync()
//        {
//            List<RfidData> dataToInsert;

//            lock (_lock)
//            {
//                dataToInsert = new List<RfidData>(_storedRfidData);
//                _storedRfidData.Clear(); // Clear after saving
//            }

//            if (dataToInsert.Count > 0)
//            {
//                await _apiService.PostRFIDRunningLogAsync(
//                    _accessToken,
//                    _userid,
//                    _recruitid,
//                    _deviceId,
//                    _location,
//                    _eventName,
//                    dataToInsert,
//                    _sessionid,
//                    _ipaddress
//                );
//            }
//        }

//        private List<string> ParseRFIDDataMultiple(byte[] buffer, int bytesRead)
//        {
//            List<string> rfidTags = new List<string>();

//            try
//            {
//                // Create a StringBuilder for the hex data
//                StringBuilder sb = new StringBuilder();
//                for (int i = 0; i < bytesRead; i++)
//                {
//                    sb.Append(buffer[i].ToString("X2")).Append(" ");
//                }

//                string hexstring = sb.ToString().Trim();

//                // Process the hexstring in chunks of 128 characters (exactly 64 bytes)
//                while (hexstring.Length >= 128)
//                {
//                    string currentSegment = hexstring.Substring(0, 128);
//                    hexstring = hexstring.Substring(128).TrimStart();

//                    // Check if the segment starts with "AA" to be considered valid
//                    if (!currentSegment.StartsWith("AA"))
//                    {
//                        continue; // Skip this segment if it does not start with "AA"
//                    }

//                    string[] hexBytes = currentSegment.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

//                    if (hexBytes.Length < 12)
//                    {
//                        continue;
//                    }

//                    for (int i = 29; i <= hexBytes.Length - 12; i += 12)
//                    {
//                        string joinedString = string.Join("", hexBytes.Skip(i).Take(12));

//                        if (joinedString.Length == 24)
//                        {
//                            rfidTags.Add(joinedString);
//                        }
//                    }
//                }

//                lock (_lock)
//                {
//                    if (_hexdataCount < MAX_DATA_COUNT)
//                    {
//                        _hexString[_hexdataCount++] = sb.ToString().Trim();
//                    }
//                    else
//                    {
//                        // Shift entries in a circular buffer fashion
//                        for (int i = 0; i < MAX_DATA_COUNT - 1; i++)
//                        {
//                            _hexString[i] = _hexString[i + 1];
//                        }
//                        _hexString[MAX_DATA_COUNT - 1] = sb.ToString().Trim();
//                    }
//                }

//                return rfidTags;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error parsing RFID data: {ex.Message}");
//                return new List<string>();
//            }
//        }

//        public void ClearData()
//        {
//            lock (_lock)
//            {
//                Array.Clear(_receivedData, 0, _receivedData.Length);
//                _dataCount = 0;
//                _processedTags.Clear();
//                _lastClearTime = DateTime.Now;
//            }
//        }


//        public RfidData[] GetReceivedData()
//        {
//            lock (_lock)
//            {
//                return _receivedData.Take(_dataCount)
//                                    .Where(d => d.Timestamp > _lastClearTime)
//                                    .ToArray();
//            }
//        }

//        public string[] GetHexData()
//        {
//            lock (_lock)
//            {
//                return _hexString.Take(_hexdataCount)
//                                    //.Where(d => d.Timestamp > _lastClearTime)
//                                    .ToArray();
//            }
//        }
//        //private int GetMaxLimitByEvent()
//        //{
//        //    if (string.IsNullOrEmpty(_eventName))
//        //        return 1000;

//        //    string ev = _eventName.ToLower();

//        //    if (ev.Contains("1600 meter running"))
//        //        return 4;

//        //    if (ev.Contains("800"))
//        //        return 2;

//        //    return 1000;
//        //}

//    }
//}

using System.Net.Sockets;
using System.Net;
using System.Text;
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

        private readonly TimeSpan _duplicatePreventionWindow = TimeSpan.FromSeconds(5);

        public bool IsRunning { get; private set; }

        private const int MAX_DATA_COUNT = 1000;
        private readonly List<RfidData> _storedRfidData = new List<RfidData>();

        public TcpListenerService(IApiService apiService, int port = 9090)
        {
            _apiService = apiService;
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _receivedData = new RfidData[MAX_DATA_COUNT];
            _hexString = new string[MAX_DATA_COUNT];
            _dataCount = 0;
            _hexdataCount = 0;
        }

        public void SetParameters(string accessToken, string userid, string recruitid,
                                  string deviceId, string location, string eventName,
                                  string ipaddress, string sessionid)
        {
            _accessToken = accessToken;
            _userid = userid;
            _recruitid = recruitid;
            _deviceId = deviceId;
            _location = location;
            _eventName = eventName;
            _sessionid = sessionid;
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

        //private async Task ProcessClientAsync(TcpClient client)
        //{
        //    using (var stream = client.GetStream())
        //    {
        //        var buffer = new byte[1024];

        //        while (client.Connected)
        //        {
        //            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        //            if (bytesRead <= 0) continue;

        //            var dataList = ParseRFIDDataMultiple(buffer, bytesRead);
        //            if (dataList == null || dataList.Count == 0) continue;

        //            var currentTime = DateTime.Now;

        //            foreach (var data in dataList)
        //            {
        //                bool shouldProcess = false;
        //                RfidData existingData = null;

        //                lock (_lock)
        //                {
        //                    existingData = _receivedData.Take(_dataCount)
        //                        .FirstOrDefault(x => x?.TagId == data);

        //                    if (existingData != null)
        //                    {
        //                        if ((currentTime - existingData.Timestamp) > _duplicatePreventionWindow)
        //                        {
        //                            existingData.Timestamp = currentTime;
        //                            existingData.LapTimes.Add(currentTime);   // Add new lap
        //                            shouldProcess = true;
        //                        }
        //                    }
        //                    else
        //                    {
        //                        shouldProcess = true;
        //                    }
        //                }

        //                if (shouldProcess)
        //                {
        //                    if (existingData == null)
        //                    {
        //                        var rfidData = new RfidData
        //                        {
        //                            TagId = data,
        //                            Timestamp = currentTime,
        //                            LapTimes = new List<DateTime> { currentTime } // First lap
        //                        };

        //                        lock (_lock)
        //                        {
        //                            if (_dataCount < MAX_DATA_COUNT)
        //                                _receivedData[_dataCount++] = rfidData;
        //                            else
        //                            {
        //                                Array.Copy(_receivedData, 1, _receivedData, 0, MAX_DATA_COUNT - 1);
        //                                _receivedData[MAX_DATA_COUNT - 1] = rfidData;
        //                            }
        //                        }

        //                        lock (_storedRfidData)
        //                        {
        //                            _storedRfidData.Add(new RfidData
        //                            {
        //                                TagId = rfidData.TagId,
        //                                Timestamp = rfidData.Timestamp,
        //                                LapTimes = new List<DateTime>(rfidData.LapTimes)
        //                            });
        //                        }
        //                    }
        //                    else
        //                    {
        //                        lock (_storedRfidData)
        //                        {
        //                            _storedRfidData.Add(new RfidData
        //                            {
        //                                TagId = existingData.TagId,
        //                                Timestamp = existingData.Timestamp,
        //                                LapTimes = new List<DateTime>(existingData.LapTimes)
        //                            });
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}
        private async Task ProcessClientAsync(TcpClient client)
        {
            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];

                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) continue;

                    var dataList = ParseRFIDDataMultiple(buffer, bytesRead);

                    if (dataList == null || dataList.Count == 0)
                        continue;

                    // Normalize ALL tagIds first
                    dataList = dataList
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim().Replace("\r", "").Replace("\n", "").ToUpper())
                        .ToList();

                    var currentTime = DateTime.Now;

                    foreach (var tagId in dataList)
                    {
                        if (string.IsNullOrWhiteSpace(tagId))
                            continue;

                        RfidData existing = null;

                        lock (_lock)
                        {
                            existing = _receivedData
                                .Take(_dataCount)
                                .FirstOrDefault(x => x.TagId == tagId);

                            if (existing == null)
                            {
                                // NEW TAG – create entry
                                existing = new RfidData
                                {
                                    TagId = tagId,
                                    Timestamp = currentTime,
                                    LapTimes = new List<DateTime> { currentTime }
                                };

                                // Put into recent list
                                if (_dataCount < MAX_DATA_COUNT)
                                    _receivedData[_dataCount++] = existing;
                                else
                                {
                                    Array.Copy(_receivedData, 1, _receivedData, 0, MAX_DATA_COUNT - 1);
                                    _receivedData[MAX_DATA_COUNT - 1] = existing;
                                }
                            }
                            else
                            {
                                if ((currentTime - existing.Timestamp) > _duplicatePreventionWindow)
                                {
                                    if (_eventName == "100 Meter Running")
                                    {
                                        // Always update timestamp, but keep ONLY ONE entry in LapTimes
                                        existing.Timestamp = currentTime;
                                        existing.LapTimes = new List<DateTime> { currentTime };
                                    }
                                    else
                                    {
                                        // Other events with lap limits
                                        int totalLaps = (_eventName == "1600 Meter Running") ? 4 :
                                                        (_eventName == "800 Meter Running") ? 2 : 1;

                                        int currentLaps = existing.LapTimes.Count;

                                        if (currentLaps < totalLaps)
                                        {
                                            existing.Timestamp = currentTime;
                                            existing.LapTimes.Add(currentTime);
                                        }
                                        else
                                        {
                                            // Max laps reached → update timestamp but do NOT add lap
                                            existing.Timestamp = currentTime;
                                        }
                                    }
                                }
                                //if ((currentTime - existing.Timestamp) > _duplicatePreventionWindow)
                                //{
                                //    int totalLaps = 1; // 100 Meter default

                                //    if (_eventName == "1600 Meter Running")
                                //    {
                                //        totalLaps = 4;
                                //    }
                                //    else if (_eventName == "800 Meter Running")
                                //    {
                                //        totalLaps = 2;
                                //    }
                                //    // 100 meter → totalLaps stays = 1 (no lap increase)

                                //    int currentLaps = existing.LapTimes.Count;

                                //    // Only allow more laps if below event limit
                                //    if (currentLaps < totalLaps)
                                //    {
                                //        existing.Timestamp = currentTime;
                                //        existing.LapTimes.Add(currentTime);
                                //    }
                                //    else
                                //    {
                                //        // Already completed allowed laps – ignore further scans
                                //        continue;
                                //    }
                                //}
                                else
                                {
                                    continue; // duplicate within 5 sec
                                }

                                // EXISTING TAG — check duplicate window
                                //if ((currentTime - existing.Timestamp) > _duplicatePreventionWindow)
                                //{
                                //    existing.Timestamp = currentTime;
                                //    existing.LapTimes.Add(currentTime);  // ADD A NEW LAP
                                //}
                                //else
                                //{
                                //    // Duplicate within 5 seconds → ignore completely
                                //    continue;
                                //}
                            }

                            // ALWAYS ADD CLEAN COPY TO STORED LIST
                            _storedRfidData.Add(new RfidData
                            {
                                TagId = existing.TagId,
                                Timestamp = existing.Timestamp,
                                LapTimes = new List<DateTime>(existing.LapTimes)
                            });
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
                _storedRfidData.Clear();
            }

            if (dataToInsert.Count > 0)
            {
                await _apiService.PostRFIDRunningLogAsync(
                    _accessToken, _userid, _recruitid, _deviceId,
                    _location, _eventName, dataToInsert,
                    _sessionid, _ipaddress
                );
            }
        }

        private List<string> ParseRFIDDataMultiple(byte[] buffer, int bytesRead)
        {
            List<string> rfidTags = new List<string>();

            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytesRead; i++)
                    sb.Append(buffer[i].ToString("X2")).Append(" ");

                string hexstring = sb.ToString().Trim();

                while (hexstring.Length >= 128)
                {
                    string currentSegment = hexstring.Substring(0, 128);
                    hexstring = hexstring.Substring(128).TrimStart();

                    if (!currentSegment.StartsWith("AA"))
                        continue;

                    string[] hexBytes = currentSegment.Split(' ',
                        StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 29; i <= hexBytes.Length - 12; i += 12)
                    {
                        string joinedString = string.Join("", hexBytes.Skip(i).Take(12));
                        if (joinedString.Length == 24)
                            rfidTags.Add(joinedString);
                    }
                }

                lock (_lock)
                {
                    if (_hexdataCount < MAX_DATA_COUNT)
                        _hexString[_hexdataCount++] = sb.ToString().Trim();
                    else
                    {
                        Array.Copy(_hexString, 1, _hexString, 0, MAX_DATA_COUNT - 1);
                        _hexString[MAX_DATA_COUNT - 1] = sb.ToString().Trim();
                    }
                }

                return rfidTags;
            }
            catch
            {
                return new List<string>();
            }
        }

        public void ClearData()
        {
            lock (_lock)
            {
                Array.Clear(_receivedData, 0, _receivedData.Length);
                _dataCount = 0;
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
                return _hexString.Take(_hexdataCount).ToArray();
            }
        }
    }
}

