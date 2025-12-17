using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RFIDReaderPortal.Models;
using RFIDReaderPortal.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RFIDReaderPortal.Controllers
{
    public class RFIDController : Controller
    {
        private readonly IApiService _apiService;
        private readonly IRFIDDiscoveryService _rfidDiscoveryService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RFIDController> _logger;

        // Single RFID service using UHF SDK only
        private static UhfRfidService _uhfService;
        private static readonly object _serviceLock = new object();

        // Configuration
        private const string READER_IP = "192.168.0.131";
        private const int READER_PORT = 2022;

        public RFIDController(
             IApiService apiService,
             IRFIDDiscoveryService rfidDiscoveryService,
             IConfiguration configuration,
             ILogger<RFIDController> logger)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _rfidDiscoveryService = rfidDiscoveryService ?? throw new ArgumentNullException(nameof(rfidDiscoveryService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> Configuration()
        {
            try
            {
                string accessToken = Request.Cookies["accesstoken"];
                string userid = Request.Cookies["UserId"];
                string recruitid = Request.Cookies["recruitid"];
                string deviceid = Request.Cookies["DeviceId"];
                string ipaddress = Request.Cookies["IpAddress"];
                string sesionid = Request.Cookies["sessionid"];

                dynamic events = await _apiService.GetAllRecruitEventsAsync(accessToken, userid, recruitid, sesionid, ipaddress);
                dynamic responsemodel = events.outcome;

                IEnumerable<RecruitmentEventDto> eventData;
                if (events.data is JObject dataObject)
                {
                    eventData = new List<RecruitmentEventDto> { dataObject.ToObject<RecruitmentEventDto>() };
                }
                else if (events.data is JArray eventDataArray)
                {
                    eventData = eventDataArray.ToObject<List<RecruitmentEventDto>>();
                }
                else
                {
                    throw new InvalidOperationException("Unexpected data type received from API");
                }

                string newtoken = responsemodel?.tokens?.ToString();
                if (!string.IsNullOrEmpty(newtoken))
                {
                    Response.Cookies.Append("accesstoken", newtoken);
                    accessToken = newtoken;
                }

                var (readerIPs, statusMessage) = await _rfidDiscoveryService.DiscoverRFIDReadersAsync();

                DeviceConfigurationDto model = new DeviceConfigurationDto
                {
                    DeviceId = readerIPs.Any() ? readerIPs.First() : "No device found",
                    statusmessage = statusMessage,
                    RecruitId = recruitid,
                    UserId = userid
                };

                Response.Cookies.Append("DeviceId", model.DeviceId);

                dynamic getAsyncResponse = await _apiService.GetAsync(accessToken, userid, model.DeviceId, sesionid, ipaddress);

                string newTokenFromGetAsync = getAsyncResponse?.outcome?.tokens?.ToString();
                if (!string.IsNullOrEmpty(newTokenFromGetAsync))
                {
                    Response.Cookies.Append("accesstoken", newTokenFromGetAsync);
                }

                List<DeviceConfigurationDto> ipDataResponse = new List<DeviceConfigurationDto>();
                if (getAsyncResponse?.data is JArray dataArray)
                {
                    ipDataResponse = dataArray.ToObject<List<DeviceConfigurationDto>>() ?? new List<DeviceConfigurationDto>();
                }

                foreach (var item in ipDataResponse)
                {
                    if (!string.IsNullOrEmpty(item.EventId))
                        Response.Cookies.Append("EventId", item.EventId);
                    if (!string.IsNullOrEmpty(item.Location))
                        Response.Cookies.Append("Location", item.Location);
                    if (!string.IsNullOrEmpty(item.eventName))
                        Response.Cookies.Append("EventName", item.eventName);
                }

                if (ipDataResponse.Count == 0)
                {
                    var viewModel = new RFIDViewModel
                    {
                        Events = eventData,
                        ReaderIPs = readerIPs,
                        StatusMessage = statusMessage
                    };
                    return View("Index", viewModel);
                }
                else
                {
                    var viewModel1 = new RFIDViewModel
                    {
                        IPDataResponse = ipDataResponse
                    };
                    return View("Reader", viewModel1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Configuration");
                return View("Error", new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        [HttpPost]
        [Consumes("application/json")]
        public async Task<IActionResult> SubmitButton([FromBody] DeviceConfigurationDto formData)
        {
            try
            {
                string accessToken = Request.Cookies["accesstoken"];
                string ipaddress = Request.Cookies["IpAddress"];
                string sesionid = Request.Cookies["sessionid"];

                if (string.IsNullOrEmpty(accessToken))
                    return BadRequest("Access token is missing.");

                if (formData == null ||
                    string.IsNullOrEmpty(formData.DeviceId) ||
                    string.IsNullOrEmpty(formData.EventId) ||
                    string.IsNullOrEmpty(formData.Location) ||
                    string.IsNullOrEmpty(formData.UserId) ||
                    string.IsNullOrEmpty(formData.RecruitId))
                {
                    return BadRequest("All input fields are required.");
                }

                dynamic InsertRFID = await _apiService.InsertDeviceConfigurationAsync(accessToken, formData, sesionid, ipaddress);

                string newToken = InsertRFID?.outcome?.tokens?.ToString();
                if (!string.IsNullOrEmpty(newToken))
                {
                    Response.Cookies.Append("accesstoken", newToken);
                }

                Response.Cookies.Append("EventName", formData.EventId);
                Response.Cookies.Append("Location", formData.Location);

                return Json(new { success = true, redirectUrl = Url.Action("Reader", "RFID") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in SubmitButton");
                return StatusCode(500, "Internal server error");
            }
        }

        public async Task<IActionResult> Reader()
        {
            try
            {
                // Initialize RFID service if needed
                EnsureRfidServiceInitialized();

                string accessToken = Request.Cookies["accesstoken"];
                string userid = Request.Cookies["UserId"];
                string deviceId = Request.Cookies["DeviceId"];
                string sesionid = Request.Cookies["sessionid"];
                string ipaddress = Request.Cookies["IpAddress"];

                dynamic getAsyncResponse = await _apiService.GetAsync(accessToken, userid, deviceId, sesionid, ipaddress);

                if (getAsyncResponse?.outcome?.tokens != null)
                {
                    string newToken = getAsyncResponse.outcome.tokens.ToString();
                    Response.Cookies.Append("accesstoken", newToken);
                }

                List<DeviceConfigurationDto> ipDataResponse = new List<DeviceConfigurationDto>();
                if (getAsyncResponse?.data != null)
                {
                    ipDataResponse = JsonConvert.DeserializeObject<List<DeviceConfigurationDto>>(
                        JsonConvert.SerializeObject(getAsyncResponse.data)
                    );
                }

                // Get RFID data
                var rfidDataArray = _uhfService?.GetAllTags() ?? Array.Empty<RfidData>();

                var viewModel = new RFIDViewModel
                {
                    RfidDataArray = rfidDataArray,
                    IsRunning = _uhfService?.IsConnected ?? false,
                    IPDataResponse = ipDataResponse
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Reader action");
                return View("Error", new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        /// <summary>
        /// Get RFID data - called by JavaScript polling
        /// Reader scans automatically via callback once connected!
        /// </summary>
        [HttpGet]
        public IActionResult GetData()
        {
            try
            {
                // Ensure service is initialized and connected
                EnsureRfidServiceInitialized();

                if (_uhfService == null)
                {
                    return Json(new
                    {
                        error = "RFID service not initialized",
                        count = 0,
                        isRunning = false,
                        isScanning = false,
                        rfidDataArray = new object[0]
                    });
                }

                if (!_uhfService.IsConnected)
                {
                    // Try to reconnect
                    bool reconnected = _uhfService.Connect(READER_IP, READER_PORT);

                    if (!reconnected)
                    {
                        return Json(new
                        {
                            error = $"RFID reader not connected. Check reader at {READER_IP}:{READER_PORT}",
                            count = 0,
                            isRunning = false,
                            isScanning = false,
                            rfidDataArray = new object[0],
                            diagnostic = _uhfService.GetDiagnosticInfo()
                        });
                    }
                }

                var rfidDataArray = _uhfService.GetAllTags();

                Console.WriteLine($"📊 GetData: Returning {rfidDataArray.Length} tags, Connected: {_uhfService.IsConnected}");

                return Json(new
                {
                    count = rfidDataArray.Length,
                    isRunning = _uhfService.IsConnected,
                    isScanning = _uhfService.IsScanning, // Always true when connected (auto-scan via callback)
                    rfidDataArray = rfidDataArray.Select(x => new
                    {
                        tagId = x.TagId,
                        lapCount = x.LapTimes.Count,
                        lastScan = x.Timestamp.ToString("HH:mm:ss.fff"),
                        lapTimes = x.LapTimes.Select(t => t.ToString("HH:mm:ss.fff")).ToList()
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetData");
                return Json(new
                {
                    error = ex.Message,
                    count = 0,
                    isRunning = false,
                    isScanning = false
                });
            }
        }

        [HttpPost]
        public IActionResult ClearData()
        {
            try
            {
                _uhfService?.ClearData();
                return Json(new { success = true, message = "Data cleared successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing data");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Reset()
        {
            try
            {
                string accessToken = Request.Cookies["accesstoken"];
                string userid = Request.Cookies["UserId"];
                string recruitid = Request.Cookies["recruitid"];
                string deviceId = Request.Cookies["DeviceId"];
                string location = Request.Cookies["Location"];
                string eventName = Request.Cookies["EventName"];
                string ipaddress = Request.Cookies["IpAddress"];
                string sessionid = Request.Cookies["sessionid"];

                var deleteResult = await _apiService.DeleteRFIDRecordsAsync(
                    accessToken, userid, recruitid, deviceId, location, eventName, sessionid, ipaddress);

                if (deleteResult?.outcome != null && !string.IsNullOrEmpty(deleteResult.outcome.tokens))
                {
                    Response.Cookies.Append("accesstoken", deleteResult.outcome.tokens);
                }

                // Clear local data
                _uhfService?.ClearData();

                return Json(new { success = true, message = "RFID records deleted and system reset." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Reset");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Test endpoint to manually add a tag
        /// </summary>
        [HttpGet]
        public IActionResult Test()
        {
            EnsureRfidServiceInitialized();

            _uhfService?.TestAddTag("E28011700000020123456789");

            var tags = _uhfService?.GetAllTags() ?? Array.Empty<RfidData>();

            return Json(new
            {
                message = "Test tag added",
                count = tags.Length,
                tags = tags.Select(x => new { x.TagId, x.Timestamp })
            });
        }

        /// <summary>
        /// Trigger a manual scan (for testing)
        /// </summary>
        [HttpPost]
        public IActionResult TriggerScan()
        {
            try
            {
                EnsureRfidServiceInitialized();

                if (_uhfService == null || !_uhfService.IsConnected)
                {
                    return Json(new { success = false, message = "Reader not connected" });
                }

                string result = _uhfService.TriggerSingleScan();

                return Json(new
                {
                    success = true,
                    message = result,
                    tagCount = _uhfService.TagCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering scan");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Diagnostic endpoint
        /// </summary>
        [HttpGet]
        public IActionResult DiagnosticStatus()
        {
            var output = new StringBuilder();
            output.AppendLine("=== RFID SYSTEM DIAGNOSTIC (UHF SDK - AUTO-SCAN MODE) ===\n");

            if (_uhfService == null)
            {
                output.AppendLine("❌ RFID Service: NOT INITIALIZED");
                output.AppendLine("\n→ Navigate to /RFID/Reader to initialize");
            }
            else
            {
                output.AppendLine($"✅ RFID Service: INITIALIZED");
                output.AppendLine($"🔌 Connected: {_uhfService.IsConnected}");
                output.AppendLine($"📡 Scanning: {(_uhfService.IsConnected ? "AUTO (via callback)" : "NO")}");
                output.AppendLine($"📊 Tags Scanned: {_uhfService.TagCount}");
                output.AppendLine($"📋 Diagnostic: {_uhfService.GetDiagnosticInfo()}\n");

                var tags = _uhfService.GetAllTags();
                if (tags.Length == 0)
                {
                    output.AppendLine("⚠️ No tags scanned yet");
                }
                else
                {
                    output.AppendLine("📁 Scanned Tags:");
                    foreach (var tag in tags)
                    {
                        output.AppendLine($"  🏷️ {tag.TagId}");
                        output.AppendLine($"     Last: {tag.Timestamp:HH:mm:ss.fff}");
                        output.AppendLine($"     Laps: {tag.LapTimes.Count}");
                    }
                }
            }

            output.AppendLine("\n=== CONFIGURATION ===");
            output.AppendLine($"Reader IP: {READER_IP}");
            output.AppendLine($"Reader Port: {READER_PORT}");
            output.AppendLine($"Scan Mode: Automatic (Callback-based)");
            output.AppendLine($"Device ID: {Request.Cookies["DeviceId"]}");
            output.AppendLine($"Location: {Request.Cookies["Location"]}");
            output.AppendLine($"Event: {Request.Cookies["EventName"]}");

            // List available SDK methods
            if (_uhfService != null)
            {
                output.AppendLine("\n=== AVAILABLE SDK METHODS ===");
                var readerType = typeof(UHFReaderModule.Reader);
                var methods = readerType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(m => !m.IsSpecialName && m.DeclaringType == readerType)
                    .OrderBy(m => m.Name);

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    output.AppendLine($"  {method.Name}({paramStr})");
                }
            }

            output.AppendLine("\n=== HOW IT WORKS ===");
            output.AppendLine("This reader uses AUTOMATIC callback-based scanning:");
            output.AppendLine("✅ Once connected, the reader continuously scans");
            output.AppendLine("✅ Tags are detected automatically via callback");
            output.AppendLine("✅ No manual start/stop needed");
            output.AppendLine("✅ Just hold tags near the antenna!");

            output.AppendLine("\n=== TROUBLESHOOTING ===");
            if (_uhfService == null)
            {
                output.AppendLine("1. Navigate to /RFID/Reader page first");
            }
            else if (!_uhfService.IsConnected)
            {
                output.AppendLine("❌ Reader not connected:");
                output.AppendLine($"   1. Verify reader is at {READER_IP}:{READER_PORT}");
                output.AppendLine("   2. Check reader is powered on");
                output.AppendLine("   3. Verify network cable connected");
                output.AppendLine($"   4. Try: ping {READER_IP}");
                output.AppendLine("   5. Check Windows Firewall");
                output.AppendLine("   6. Verify reader supports TCP connection on port 2022");
            }
            else if (_uhfService.TagCount == 0)
            {
                output.AppendLine("✅ Reader connected and scanning automatically!");
                output.AppendLine("   → Hold RFID tag VERY close to antenna (2-3 inches)");
                output.AppendLine("   → Check reader LED indicator for activity");
                output.AppendLine("   → Verify tags are UHF (860-960 MHz)");
                output.AppendLine("   → Try test endpoint: /RFID/Test");
                output.AppendLine("   → Check antenna cable connection");
                output.AppendLine("   → Some readers need to be 'armed' first");
            }
            else
            {
                output.AppendLine("✅ Everything working correctly!");
                output.AppendLine($"   {_uhfService.TagCount} tag(s) detected successfully");
            }

            output.AppendLine("\n=== TEST ENDPOINTS ===");
            output.AppendLine("/RFID/Test - Add manual test tag");
            output.AppendLine("/RFID/GetData - Get current tag data (JSON)");
            output.AppendLine("/RFID/DiagnosticStatus - This page");

            return Content(output.ToString(), "text/plain");
        }

        /// <summary>
        /// Ensure RFID service is initialized and connected
        /// </summary>
        private void EnsureRfidServiceInitialized()
        {
            lock (_serviceLock)
            {
                if (_uhfService == null)
                {
                    Console.WriteLine("🔧 Initializing UHF RFID Service...");
                    _uhfService = new UhfRfidService();
                }

                if (!_uhfService.IsConnected)
                {
                    Console.WriteLine($"🔌 Connecting to reader at {READER_IP}:{READER_PORT}...");
                    bool connected = _uhfService.Connect(READER_IP, READER_PORT);

                    if (connected)
                    {
                        Console.WriteLine("✅ Reader connected and AUTO-SCANNING via callback!");
                        Console.WriteLine("   📡 Tags will be detected automatically");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Failed to connect to {READER_IP}:{READER_PORT}");
                    }
                }
            }
        }
    }
}