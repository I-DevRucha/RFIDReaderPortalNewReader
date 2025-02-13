namespace RFIDReaderPortal.Services
{
    public interface IRFIDDiscoveryService 
    {
        Task<List<string>> DiscoverRFIDReadersAsync();
    }
}