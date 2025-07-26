using System.ComponentModel.DataAnnotations;
using System.Net;
using Newtonsoft.Json;

namespace PerunNetworkManager.Core.Models
{
    public class NetworkProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;
        
        public string Description { get; set; } = string.Empty;
        
        public string AdapterName { get; set; } = string.Empty;
        
        public bool UseDHCP { get; set; } = true;
        
        [Display(Name = "IP Address")]
        public string? IPAddress { get; set; }
        
        [Display(Name = "Subnet Mask")]
        public string? SubnetMask { get; set; }
        
        [Display(Name = "Default Gateway")]
        public string? DefaultGateway { get; set; }
        
        [Display(Name = "Primary DNS")]
        public string? PrimaryDNS { get; set; }
        
        [Display(Name = "Secondary DNS")]
        public string? SecondaryDNS { get; set; }
        
        [Display(Name = "Primary WINS")]
        public string? PrimaryWINS { get; set; }
        
        [Display(Name = "Secondary WINS")]
        public string? SecondaryWINS { get; set; }
        
        public string? ComputerName { get; set; }
        
        public string? WorkgroupDomain { get; set; }
        
        public bool IsDomain { get; set; } = false;
        
        public string? DomainUsername { get; set; }
        
        [JsonIgnore]
        public string? DomainPassword { get; set; }
        
        public List<PrinterConfiguration> Printers { get; set; } = new();
        
        public ProxyConfiguration Proxy { get; set; } = new();
        
        public string? MacAddress { get; set; }
        
        public List<CustomScript> Scripts { get; set; } = new();
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        public DateTime LastModified { get; set; } = DateTime.Now;
        
        public bool IsActive { get; set; } = false;
        
        public ProfileIcon Icon { get; set; } = ProfileIcon.Ethernet;
        
        public string Notes { get; set; } = string.Empty;
        
        public bool AutoApplyOnStartup { get; set; } = false;
        
        public List<string> TriggerNetworks { get; set; } = new();
        
        public TimeSpan? ScheduledTime { get; set; }
        
        public DaysOfWeek ScheduledDays { get; set; } = DaysOfWeek.None;
        
        // Validation methods
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;
                
            if (!UseDHCP)
            {
                if (!IsValidIPAddress(IPAddress) || 
                    !IsValidIPAddress(SubnetMask) || 
                    !IsValidIPAddress(DefaultGateway))
                    return false;
            }
            
            if (!string.IsNullOrEmpty(PrimaryDNS) && !IsValidIPAddress(PrimaryDNS))
                return false;
                
            if (!string.IsNullOrEmpty(SecondaryDNS) && !IsValidIPAddress(SecondaryDNS))
                return false;
                
            return true;
        }
        
        private static bool IsValidIPAddress(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;
                
            return IPAddress.TryParse(ipAddress, out _);
        }
        
        public NetworkProfile Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            var clone = JsonConvert.DeserializeObject<NetworkProfile>(json)!;
            clone.Id = Guid.NewGuid();
            clone.Name = $"{Name} - Copy";
            clone.IsActive = false;
            return clone;
        }
    }
    
    public class PrinterConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDefault { get; set; } = false;
        public bool InstallDriver { get; set; } = false;
    }
    
    public class ProxyConfiguration
    {
        public bool UseProxy { get; set; } = false;
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; } = 8080;
        public string Username { get; set; } = string.Empty;
        
        [JsonIgnore]
        public string Password { get; set; } = string.Empty;
        
        public string BypassList { get; set; } = "*.local;127.*;10.*;172.16.*;192.168.*";
        public bool BypassLocal { get; set; } = true;
    }
    
    public class CustomScript
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public ScriptType Type { get; set; } = ScriptType.PowerShell;
        public ScriptTrigger Trigger { get; set; } = ScriptTrigger.BeforeApply;
        public bool Enabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
    }
    
    public enum ProfileIcon
    {
        Ethernet,
        WiFi,
        VPN,
        Cellular,
        Bluetooth,
        Server,
        Home,
        Office,
        Public,
        Custom
    }
    
    public enum ScriptType
    {
        PowerShell,
        Batch,
        Executable
    }
    
    public enum ScriptTrigger
    {
        BeforeApply,
        AfterApply,
        BeforeRestore,
        AfterRestore
    }
    
    [Flags]
    public enum DaysOfWeek
    {
        None = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 4,
        Thursday = 8,
        Friday = 16,
        Saturday = 32,
        Sunday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekends = Saturday | Sunday,
        All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
    }
}
