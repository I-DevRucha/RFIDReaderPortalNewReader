using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Linq;
using System.Net;

namespace RFIDReaderPortal.Services
{
    public class RFIDDiscoveryService : IRFIDDiscoveryService
    {
        public async Task<List<string>> DiscoverRFIDReadersAsync()
        {
            var ipAddresses = new List<string>();

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) &&
                    !ni.Description.ToLower().Contains("virtual") &&
                    !ni.Description.ToLower().Contains("pseudo"))
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var ip in ipProps.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipAddresses.Add(ip.Address.ToString()); 
                        }
                    }
                }
            }

            // If no Ethernet IP found, try to get any physical adapter IP
            if (ipAddresses.Count == 0)
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !ni.Description.ToLower().Contains("virtual") &&
                        !ni.Description.ToLower().Contains("pseudo"))
                    {
                        var ipProps = ni.GetIPProperties();
                        foreach (var ip in ipProps.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ipAddresses.Add(ip.Address.ToString());
                            }
                        }
                    }
                }
            }

            // Simulate asynchronous operation
            await Task.Delay(100);

            return ipAddresses;
        }
    }
}