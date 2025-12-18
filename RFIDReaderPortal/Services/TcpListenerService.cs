

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
        private async Task ProcessClientAsync(TcpClient client)
{
    using (var stream = client.GetStream())
    {
        var buffer = new byte[4096];

        while (client.Connected)
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0) continue;

            // 1️⃣ RAW → HEX
            string hexData = BitConverter.ToString(buffer, 0, bytesRead)
                .Replace("-", "")
                .ToUpperInvariant();

            // 2️⃣ Extract EPCs
            var epcs = ExtractEpcs(hexData);
            if (epcs.Count == 0) continue;

            var now = DateTime.Now;

                    foreach (var epc in epcs)
                    {
                        bool shouldStore = false;
                        RfidData existing;

                        lock (_lock)
                        {
                            existing = _receivedData
                                .Take(_dataCount)
                                .FirstOrDefault(x => x.TagId == epc);

                            if (existing == null)
                            {
                                // FIRST SCAN
                                existing = new RfidData
                                {
                                    TagId = epc,
                                    Timestamp = now,
                                    LapTimes = new List<DateTime> { now }
                                };

                                if (_dataCount < MAX_DATA_COUNT)
                                    _receivedData[_dataCount++] = existing;
                                else
                                {
                                    Array.Copy(_receivedData, 1, _receivedData, 0, MAX_DATA_COUNT - 1);
                                    _receivedData[MAX_DATA_COUNT - 1] = existing;
                                }

                                shouldStore = true;
                            }
                            else if ((now - existing.Timestamp) > _duplicatePreventionWindow)
                            {
                                if (_eventName == "100 Meter Running")
                                {
                                    // ✅ ALWAYS update time for 100 meter
                                    existing.Timestamp = now;

                                    if (existing.LapTimes.Count == 0)
                                        existing.LapTimes.Add(now);
                                    else
                                        existing.LapTimes[0] = now; // overwrite

                                    shouldStore = true;
                                }
                                else
                                {
                                    //                    int maxLaps = _eventName == "1600 Meter Running" ? 5 :
                                    //_eventName == "800 Meter Running" ? 3 :
                                    //1;

                                    //                    // Make sure LapTimes list has enough capacity
                                    //                    while (existing.LapTimes.Count < maxLaps)
                                    //                        existing.LapTimes.Add(null);

                                    //                    // Find first empty slot (null) for this scan
                                    //                    int nextLapIndex = existing.LapTimes.FindIndex(t => t == null);

                                    //                    if (nextLapIndex >= 0)
                                    //                    {
                                    //                        existing.LapTimes[nextLapIndex] = now; // assign current lap time
                                    //                        existing.Timestamp = now;
                                    //                        shouldStore = true;
                                    //                    }

                                    int maxLaps =
                                        _eventName == "1600 Meter Running" ? 5 :
                                        _eventName == "800 Meter Running" ? 3 :
                                        1;

                                    if (existing.LapTimes.Count < maxLaps)
                                    {
                                        existing.Timestamp = now;
                                        existing.LapTimes.Add(now);
                                        shouldStore = true;
                                    }
                                }
                            }

                            //else if ((now - existing.Timestamp) > _duplicatePreventionWindow)
                            //{
                            //    int maxLaps =
                            //        _eventName == "1600 Meter Running" ? 4 :
                            //        _eventName == "800 Meter Running" ? 2 :
                            //        1; // 100 Meter

                            //    if (existing.LapTimes.Count < maxLaps)
                            //    {
                            //        existing.Timestamp = now;
                            //        existing.LapTimes.Add(now);
                            //        shouldStore = true;
                            //    }
                            //}

                            // ONLY store when something actually changed
                            if (shouldStore)
                            {
                                _storedRfidData.Add(new RfidData
                                {
                                    TagId = existing.TagId,
                                    Timestamp = existing.Timestamp,
                                    LapTimes = new List<DateTime>(existing.LapTimes)
                                });
                            }
                        }

                        Console.WriteLine($"🏷️ EPC RECEIVED: {epc}");
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
        private static List<string> ExtractEpcs(string hex)
        {
            const int EPC_LEN = 24; // 12 bytes
            const string EPC_PREFIX = "E280117000000212AC9";

            HashSet<string> result = new();

            if (string.IsNullOrWhiteSpace(hex))
                return result.ToList();

            hex = hex.ToUpperInvariant();

            for (int i = 0; i <= hex.Length - EPC_LEN; i += 2)
            {
                string candidate = hex.Substring(i, EPC_LEN);

                if (
                    candidate.StartsWith(EPC_PREFIX) &&
                    candidate.All(c => "0123456789ABCDEF".Contains(c))
                )
                {
                    result.Add(candidate);
                }
            }

            return result.ToList();
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

