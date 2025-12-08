namespace RFIDReaderPortal.Models
{
    public class RfidData
    {
        public string? TagId { get; set; }
        public string? LapNo { get; set; }
        public DateTime Timestamp { get; set; }
        // NEW —— store all laps (timestamps)
        public List<DateTime> LapTimes { get; set; } = new List<DateTime>();
    }
}