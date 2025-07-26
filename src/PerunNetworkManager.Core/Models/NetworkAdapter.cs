using System.Net.NetworkInformation;

namespace PerunNetworkManager.Core.Models
{
    public class NetworkAdapter
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public NetworkInterfaceType InterfaceType { get; set; }
        public OperationalStatus Status { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public long Speed { get; set; }
        public bool SupportsMulticast { get; set; }
        public bool IsReceiveOnly { get; set; }
        public List<string> IPAddresses { get; set; } = new();
        public List<string> SubnetMasks { get; set; } = new();
        public List<string> Gateways { get; set; } = new();
        public List<string> DnsServers { get; set; } = new();
        public string DHCPServer { get; set; } = string.Empty;
        public bool IsDhcpEnabled { get; set; }
        public DateTime? DhcpLeaseObtained { get; set; }
        public DateTime? DhcpLeaseExpires { get; set; }
        public NetworkStatistics Statistics { get; set; } = new();
        public bool IsEnabled { get; set; } = true;
        public string DriverVersion { get; set; } = string.Empty;
        public DateTime DriverDate { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        
        public bool IsWireless => InterfaceType == NetworkInterfaceType.Wireless80211;
        public bool IsEthernet => InterfaceType == NetworkInterfaceType.Ethernet;
        public bool IsVirtual => Name.Contains("Virtual") || Name.Contains("VMware") || Name.Contains("VirtualBox");
        
        public string StatusText => Status switch
        {
            OperationalStatus.Up => "Connected",
            OperationalStatus.Down => "Disconnected",
            OperationalStatus.Testing => "Testing",
            OperationalStatus.Unknown => "Unknown",
            OperationalStatus.Dormant => "Dormant",
            OperationalStatus.NotPresent => "Not Present",
            OperationalStatus.LowerLayerDown => "Cable Unplugged",
            _ => Status.ToString()
        };
        
        public string SpeedText
        {
            get
            {
                if (Speed <= 0) return "Unknown";
                
                return Speed switch
                {
                    >= 1_000_000_000 => $"{Speed / 1_000_000_000} Gbps",
                    >= 1_000_000 => $"{Speed / 1_000_000} Mbps",
                    >= 1_000 => $"{Speed / 1_000} Kbps",
                    _ => $"{Speed}
