using System.Net;
using System.Net.NetworkInformation;

namespace PerunNetworkManager.Core.Models
{
    public class ScannedDevice
    {
        public IPAddress IPAddress { get; set; } = IPAddress.None;
        public string MacAddress { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public string NetBiosName { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
        public long ResponseTime { get; set; } = -1;
        public bool IsReachable { get; set; } = false;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public List<int> OpenPorts { get; set; } = new();
        public List<string> Services { get; set; } = new();
        public string OperatingSystem { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool SupportsWol { get; set; } = false;
        public PingReply? LastPingResult { get; set; }
        public string Notes { get; set; } = string.Empty;
        public bool IsWatched { get; set; } = false;
        public DeviceStatus Status { get; set; } = DeviceStatus.Online;
        
        public string IPAddressString => IPAddress?.ToString() ?? "Unknown";
        
        public string ResponseTimeText
        {
            get
            {
                if (ResponseTime < 0) return "N/A";
                if (ResponseTime == 0) return "<1ms";
                return $"{ResponseTime}ms";
            }
        }
        
        public string StatusText => Status switch
        {
            DeviceStatus.Online => "Online",
            DeviceStatus.Offline => "Offline",
            DeviceStatus.Unknown => "Unknown",
            DeviceStatus.Responding => "Responding",
            DeviceStatus.NotResponding => "Not Responding",
            _ => Status.ToString()
        };
        
        public DeviceIcon GetIcon()
        {
            return DeviceType switch
            {
                DeviceType.Router => DeviceIcon.Router,
                DeviceType.Switch => DeviceIcon.Switch,
                DeviceType.AccessPoint => DeviceIcon.AccessPoint,
                DeviceType.Computer => DeviceIcon.Computer,
                DeviceType.Server => DeviceIcon.Server,
                DeviceType.Printer => DeviceIcon.Printer,
                DeviceType.Mobile => DeviceIcon.Mobile,
                DeviceType.Tablet => DeviceIcon.Tablet,
                DeviceType.IoT => DeviceIcon.IoT,
                DeviceType.Camera => DeviceIcon.Camera,
                DeviceType.NAS => DeviceIcon.NAS,
                DeviceType.MediaDevice => DeviceIcon.MediaDevice,
                DeviceType.GameConsole => DeviceIcon.GameConsole,
                DeviceType.SmartTV => DeviceIcon.SmartTV,
                _ => DeviceIcon.Unknown
            };
        }
        
        public void UpdateFromPing(PingReply pingReply)
        {
            LastPingResult = pingReply;
            LastSeen = DateTime.Now;
            
            if (pingReply.Status == IPStatus.Success)
            {
                IsReachable = true;
                ResponseTime = pingReply.RoundtripTime;
                Status = DeviceStatus.Online;
            }
            else
            {
                IsReachable = false;
                ResponseTime = -1;
                Status = DeviceStatus.Offline;
            }
        }
        
        public void DetermineDeviceType()
        {
            // Determine device type based on various factors
            if (OpenPorts.Contains(80) || OpenPorts.Contains(443) || OpenPorts.Contains(8080))
            {
                if (OpenPorts.Contains(22) || OpenPorts.Contains(23))
                {
                    if (Vendor.Contains("Cisco") || Vendor.Contains("Netgear") || Vendor.Contains("Linksys"))
                        DeviceType = DeviceType.Router;
                    else
                        DeviceType = DeviceType.Server;
                }
                else if (NetBiosName.ToLower().Contains("printer") || OpenPorts.Contains(515) || OpenPorts.Contains(631))
                {
                    DeviceType = DeviceType.Printer;
                }
                else if (Services.Any(s => s.Contains("camera") || s.Contains("surveillance")))
                {
                    DeviceType = DeviceType.Camera;
                }
                else
                {
                    DeviceType = DeviceType.Server;
                }
            }
            else if (OpenPorts.Contains(22) || OpenPorts.Contains(23) || OpenPorts.Contains(161))
            {
                if (Vendor.Contains("Cisco") || Vendor.Contains("HP") || Vendor.Contains("Dell") || 
                    NetBiosName.ToLower().Contains("switch"))
                    DeviceType = DeviceType.Switch;
                else
                    DeviceType = DeviceType.Router;
            }
            else if (OpenPorts.Contains(515) || OpenPorts.Contains(631) || OpenPorts.Contains(9100))
            {
                DeviceType = DeviceType.Printer;
            }
            else if (OpenPorts.Contains(135) || OpenPorts.Contains(139) || OpenPorts.Contains(445))
            {
                DeviceType = DeviceType.Computer;
            }
            else if (OpenPorts.Contains(554) || OpenPorts.Contains(1935))
            {
                DeviceType = DeviceType.MediaDevice;
            }
            else if (Vendor.Contains("Apple") && (NetBiosName.Contains("iPhone") || NetBiosName.Contains("iPad")))
            {
                DeviceType = NetBiosName.Contains("iPad") ? DeviceType.Tablet : DeviceType.Mobile;
            }
            else if (Vendor.Contains("Samsung") || Vendor.Contains("LG") || Vendor.Contains("Sony"))
            {
                if (OpenPorts.Contains(8080) || OpenPorts.Contains(7676))
                    DeviceType = DeviceType.SmartTV;
                else
                    DeviceType = DeviceType.Mobile;
            }
            else if (NetBiosName.ToLower().Contains("nas") || Services.Any(s => s.Contains("SMB") || s.Contains("NFS")))
            {
                DeviceType = DeviceType.NAS;
            }
            else
            {
                DeviceType = DeviceType.Unknown;
            }
        }
        
        public Dictionary<string, object> ToExportData()
        {
            return new Dictionary<string, object>
            {
                ["IP Address"] = IPAddressString,
                ["MAC Address"] = MacAddress,
                ["Host Name"] = HostName,
                ["NetBIOS Name"] = NetBiosName,
                ["Vendor"] = Vendor,
                ["Device Type"] = DeviceType.ToString(),
                ["Response Time"] = ResponseTimeText,
                ["Status"] = StatusText,
                ["Open Ports"] = string.Join(", ", OpenPorts),
                ["Services"] = string.Join(", ", Services),
                ["Operating System"] = OperatingSystem,
                ["First Seen"] = FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
                ["Last Seen"] = LastSeen.ToString("yyyy-MM-dd HH:mm:ss"),
                ["Notes"] = Notes
            };
        }
    }
    
    public enum DeviceType
    {
        Unknown,
        Computer,
        Server,
        Router,
        Switch,
        AccessPoint,
        Printer,
        Mobile,
        Tablet,
        IoT,
        Camera,
        NAS,
        MediaDevice,
        GameConsole,
        SmartTV,
        VoIP
    }
    
    public enum DeviceStatus
    {
        Unknown,
        Online,
        Offline,
        Responding,
        NotResponding
    }
    
    public enum DeviceIcon
    {
        Unknown,
        Computer,
        Server,
        Router,
        Switch,
        AccessPoint,
        Printer,
        Mobile,
        Tablet,
        IoT,
        Camera,
        NAS,
        MediaDevice,
        GameConsole,
        SmartTV
    }
    
    public class SubnetInfo
    {
        public IPAddress NetworkAddress { get; set; } = IPAddress.None;
        public IPAddress SubnetMask { get; set; } = IPAddress.None;
        public int CIDR { get; set; }
        public IPAddress FirstUsableIP { get; set; } = IPAddress.None;
        public IPAddress LastUsableIP { get; set; } = IPAddress.None;
        public IPAddress BroadcastAddress { get; set; } = IPAddress.None;
        public int TotalHosts { get; set; }
        public int UsableHosts { get; set; }
        public string AdapterName { get; set; } = string.Empty;
        public List<ScannedDevice> Devices { get; set; } = new();
        
        public string NetworkRange => $"{NetworkAddress}/{CIDR}";
        public string UsableRange => $"{FirstUsableIP} - {LastUsableIP}";
        
        public static SubnetInfo FromIPAndMask(IPAddress ip, IPAddress mask)
        {
            var subnet = new SubnetInfo();
            
            // Calculate network address
            var ipBytes = ip.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            var networkBytes = new byte[4];
            
            for (int i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }
            
            subnet.NetworkAddress = new IPAddress(networkBytes);
            subnet.SubnetMask = mask;
            
            // Calculate CIDR
            subnet.CIDR = CountSetBits(mask);
            
            // Calculate broadcast address
            var broadcastBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(networkBytes[i] | (~maskBytes[i] & 0xFF));
            }
            subnet.BroadcastAddress = new IPAddress(broadcastBytes);
            
            // Calculate usable range
            var firstBytes = (byte[])networkBytes.Clone();
            firstBytes[3] += 1;
            subnet.FirstUsableIP = new IPAddress(firstBytes);
            
            var lastBytes = (byte[])broadcastBytes.Clone();
            lastBytes[3] -= 1;
            subnet.LastUsableIP = new IPAddress(lastBytes);
            
            // Calculate host counts
            subnet.TotalHosts = (int)Math.Pow(2, 32 - subnet.CIDR);
            subnet.UsableHosts = subnet.TotalHosts - 2; // Exclude network and broadcast
            
            return subnet;
        }
        
        private static int CountSetBits(IPAddress mask)
        {
            var bytes = mask.GetAddressBytes();
            int count = 0;
            
            foreach (var b in bytes)
            {
                for (int i = 0; i < 8; i++)
                {
                    if ((b & (1 << (7 - i))) != 0)
                        count++;
                    else
                        break;
                }
            }
            
            return count;
        }
        
        public bool ContainsIP(IPAddress ip)
        {
            var ipBytes = ip.GetAddressBytes();
            var networkBytes = NetworkAddress.GetAddressBytes();
            var maskBytes = SubnetMask.GetAddressBytes();
            
            for (int i = 0; i < 4; i++)
            {
                if ((ipBytes[i] & maskBytes[i]) != networkBytes[i])
                    return false;
            }
            
            return true;
        }
        
        public List<IPAddress> GetAllIPs()
        {
            var ips = new List<IPAddress>();
            var firstBytes = FirstUsableIP.GetAddressBytes();
            var lastBytes = LastUsableIP.GetAddressBytes();
            
            // Convert to uint for easier arithmetic
            uint first = BitConverter.ToUInt32(firstBytes.Reverse().ToArray(), 0);
            uint last = BitConverter.ToUInt32(lastBytes.Reverse().ToArray(), 0);
            
            for (uint i = first; i <= last; i++)
            {
                var bytes = BitConverter.GetBytes(i).Reverse().ToArray();
                ips.Add(new IPAddress(bytes));
            }
            
            return ips;
        }
    }
}
