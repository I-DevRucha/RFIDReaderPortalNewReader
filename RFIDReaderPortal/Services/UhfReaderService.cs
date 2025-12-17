using System;
using System.Collections.Generic;
using System.Linq;
using RFIDReaderPortal.Models;
using UHFReaderModule;

namespace RFIDReaderPortal.Services
{
    public class UhfRfidService
    {
        private Reader _reader;
        private byte _comAddr = 0xFF;

        private readonly object _lock = new();
        private readonly Dictionary<string, RfidData> _tagData = new();
        private readonly TimeSpan _duplicateWindow = TimeSpan.FromSeconds(3);

        public bool IsConnected { get; private set; }
        public bool IsScanning => IsConnected; // Callback-based readers scan automatically when connected
        public int TagCount => _tagData.Count;

        /// <summary>
        /// Connect to RFID reader via TCP
        /// This reader uses callback-based scanning - it scans automatically once connected!
        /// </summary>
        public bool Connect(string ip, int port)
        {
            try
            {
                Console.WriteLine($"🔌 Connecting to RFID reader at {ip}:{port}...");

                _reader = new Reader();
                int result = _reader.OpenByTcp(ip, port, ref _comAddr);

                if (result != 0)
                {
                    Console.WriteLine($"❌ Connection failed with error code: {result}");
                    return false;
                }

                // Set callback to receive tag data
                // The reader will automatically call this whenever it detects a tag
                _reader.ReceiveCallback = OnTagReceived;

                IsConnected = true;
                Console.WriteLine($"✅ Connected successfully! Device address: 0x{_comAddr:X2}");

                // Try to start real-time inventory using reflection (different SDKs use different method names)
                TryStartInventory();

                Console.WriteLine($"📡 Reader is now scanning for tags!");
                Console.WriteLine($"   → Hold an RFID tag near the antenna");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Connection exception: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Try to start inventory using various SDK method names
        /// </summary>
        private void TryStartInventory()
        {
            if (_reader == null) return;

            try
            {
                Console.WriteLine("\n🔍 Searching for inventory methods...");

                // Get all public methods
                var allMethods = _reader.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                Console.WriteLine($"📋 Found {allMethods.Length} total methods in Reader class");

                // Filter to likely inventory methods
                var inventoryMethods = allMethods
                    .Where(m => m.Name.ToLower().Contains("inventory") ||
                               m.Name.ToLower().Contains("scan") ||
                               m.Name.ToLower().Contains("read") ||
                               m.Name.ToLower().Contains("tag"))
                    .ToList();

                Console.WriteLine($"📋 Found {inventoryMethods.Count} potential inventory methods:");
                foreach (var method in inventoryMethods)
                {
                    var parameters = method.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Console.WriteLine($"   - {method.Name}({paramStr})");
                }

                // Try common method names in order of likelihood
                var methodNames = new[]
                {
                    "RealTimeInventory",
                    "InventoryReal",
                    "StartInventory",
                    "Inventory6C",
                    "InventoryG2",
                    "Inventory",
                    "StartRead",
                    "BeginInventory"
                };

                foreach (var methodName in methodNames)
                {
                    try
                    {
                        var method = _reader.GetType().GetMethod(methodName);
                        if (method != null)
                        {
                            var parameters = method.GetParameters();

                            Console.WriteLine($"🎯 Trying method: {methodName} with {parameters.Length} parameters");

                            if (parameters.Length == 2)
                            {
                                // Most common: (byte comAddr, byte repeatTime)
                                var result = method.Invoke(_reader, new object[] { _comAddr, (byte)0xFF });
                                Console.WriteLine($"✅ Started inventory using {methodName}(comAddr={_comAddr}, repeatTime=0xFF)");
                                Console.WriteLine($"   Result: {result}");
                                return;
                            }
                            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(byte))
                            {
                                // Some use: (byte comAddr)
                                var result = method.Invoke(_reader, new object[] { _comAddr });
                                Console.WriteLine($"✅ Started inventory using {methodName}(comAddr={_comAddr})");
                                Console.WriteLine($"   Result: {result}");
                                return;
                            }
                            else if (parameters.Length == 0)
                            {
                                // Parameterless
                                var result = method.Invoke(_reader, new object[] { });
                                Console.WriteLine($"✅ Started inventory using {methodName}()");
                                Console.WriteLine($"   Result: {result}");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️ Method {methodName} failed: {ex.Message}");
                    }
                }

                Console.WriteLine("\n⚠️ No standard inventory method found");
                Console.WriteLine("💡 This reader may use passive callback mode only");
                Console.WriteLine("   The callback should still work when tags are in range");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error during method discovery: {ex.Message}");
            }
        }

        /// <summary>
        /// Connect via COM port
        /// </summary>
        public bool Connect(int comPort, byte baudRate = 5)
        {
            try
            {
                Console.WriteLine($"🔌 Connecting to COM{comPort}...");

                _reader = new Reader();
                int result = _reader.OpenByCom(comPort, ref _comAddr, baudRate);

                if (result != 0)
                {
                    Console.WriteLine($"❌ Connection failed with error code: {result}");
                    return false;
                }

                _reader.ReceiveCallback = OnTagReceived;
                IsConnected = true;

                Console.WriteLine($"✅ Connected to COM{comPort}!");
                Console.WriteLine($"📡 Reader scanning automatically via callback!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Connection exception: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Callback when tag is read by the SDK
        /// This is called AUTOMATICALLY by the reader hardware whenever a tag is detected
        /// </summary>
        private void OnTagReceived(RFIDTag tag)
        {
            try
            {
                Console.WriteLine($"🎯 CALLBACK TRIGGERED! Received tag object");

                if (tag == null)
                {
                    Console.WriteLine("❌ Tag object is NULL");
                    return;
                }

                Console.WriteLine($"📋 Tag properties: UID={tag.UID ?? "NULL"}, ANT={tag.ANT}, RSSI={tag.RSSI}");

                if (string.IsNullOrEmpty(tag.UID))
                {
                    Console.WriteLine("⚠️ Tag UID is null or empty");
                    return;
                }

                string tagId = tag.UID;
                DateTime now = DateTime.Now;

                Console.WriteLine($"📡 RAW TAG DETECTED: {tagId}, ANT: {tag.ANT}, RSSI: {tag.RSSI}");

                lock (_lock)
                {
                    if (_tagData.ContainsKey(tagId))
                    {
                        var existing = _tagData[tagId];

                        // Check if outside duplicate window
                        if ((now - existing.Timestamp) > _duplicateWindow)
                        {
                            existing.Timestamp = now;
                            existing.LapTimes.Add(now);

                            Console.WriteLine($"🔄 LAP #{existing.LapTimes.Count}: {tagId} @ {now:HH:mm:ss.fff}");
                        }
                        else
                        {
                            Console.WriteLine($"⏭️ Duplicate ignored (within {_duplicateWindow.TotalSeconds}s)");
                        }
                    }
                    else
                    {
                        // New tag
                        var rfidData = new RfidData
                        {
                            TagId = tagId,
                            Timestamp = now,
                            LapTimes = new List<DateTime> { now }
                        };

                        _tagData[tagId] = rfidData;
                        Console.WriteLine($"🆕 NEW TAG SCANNED: {tagId} @ {now:HH:mm:ss.fff} (Total tags: {_tagData.Count})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing tag: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Get all scanned tags
        /// </summary>
        public RfidData[] GetAllTags()
        {
            lock (_lock)
            {
                var result = _tagData.Values.ToArray();
                Console.WriteLine($"📊 GetAllTags: Returning {result.Length} tags");
                return result;
            }
        }

        /// <summary>
        /// Get specific tag by ID
        /// </summary>
        public RfidData GetTag(string tagId)
        {
            lock (_lock)
            {
                return _tagData.ContainsKey(tagId) ? _tagData[tagId] : null;
            }
        }

        /// <summary>
        /// Clear all stored tag data
        /// </summary>
        public void ClearData()
        {
            lock (_lock)
            {
                int count = _tagData.Count;
                _tagData.Clear();
                Console.WriteLine($"🧹 Cleared {count} tags");
            }
        }

        /// <summary>
        /// Disconnect from reader
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected || _reader == null)
                return;

            try
            {
                Console.WriteLine("🔌 Disconnecting from reader...");

                if (_reader.DevName != null && _reader.DevName.StartsWith("COM"))
                    _reader.CloseByCom();
                else
                    _reader.CloseByTcp();

                Console.WriteLine("✅ Disconnected successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Disconnect error: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
                _reader = null;
            }
        }

        /// <summary>
        /// Manual test method - simulates a tag scan
        /// </summary>
        public void TestAddTag(string tagId)
        {
            try
            {
                Console.WriteLine($"🧪 TEST: Adding manual tag {tagId}");

                DateTime now = DateTime.Now;

                lock (_lock)
                {
                    if (_tagData.ContainsKey(tagId))
                    {
                        var existing = _tagData[tagId];

                        if ((now - existing.Timestamp) > _duplicateWindow)
                        {
                            existing.Timestamp = now;
                            existing.LapTimes.Add(now);
                            Console.WriteLine($"🔄 TEST LAP #{existing.LapTimes.Count}: {tagId}");
                        }
                    }
                    else
                    {
                        var rfidData = new RfidData
                        {
                            TagId = tagId,
                            Timestamp = now,
                            LapTimes = new List<DateTime> { now }
                        };

                        _tagData[tagId] = rfidData;
                        Console.WriteLine($"🆕 TEST TAG ADDED: {tagId} (Total: {_tagData.Count})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in TestAddTag: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Get diagnostic info
        /// </summary>
        public string GetDiagnosticInfo()
        {
            lock (_lock)
            {
                string scanStatus = IsConnected ? "Auto-scanning via callback" : "Not connected";
                return $"Connected: {IsConnected}, Scanning: {scanStatus}, Tags: {_tagData.Count}, Reader: {_reader?.DevName ?? "None"}";
            }
        }

        /// <summary>
        /// Manually trigger a single inventory scan (for testing)
        /// </summary>
        public string TriggerSingleScan()
        {
            if (!IsConnected || _reader == null)
            {
                return "❌ Not connected";
            }

            try
            {
                Console.WriteLine("🔍 Triggering manual inventory scan...");

                // Try Inventory6C method (common for single scans)
                var method = _reader.GetType().GetMethod("Inventory6C");
                if (method != null)
                {
                    var result = method.Invoke(_reader, new object[] { _comAddr });
                    Console.WriteLine($"✅ Manual scan triggered via Inventory6C");
                    return "Scan triggered";
                }

                // Try Inventory method
                method = _reader.GetType().GetMethod("Inventory");
                if (method != null)
                {
                    var result = method.Invoke(_reader, new object[] { _comAddr });
                    Console.WriteLine($"✅ Manual scan triggered via Inventory");
                    return "Scan triggered";
                }

                return "⚠️ No scan method available";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Manual scan error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }
    }
}



