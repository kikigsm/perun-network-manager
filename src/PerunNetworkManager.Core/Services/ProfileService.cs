using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PerunNetworkManager.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PerunNetworkManager.Core.Services
{
    public class ProfileService
    {
        private readonly ILogger<ProfileService> _logger;
        private readonly string _profilesDirectory;
        private readonly string _profilesFile;
        private readonly string _backupDirectory;

        public ProfileService(ILogger<ProfileService> logger)
        {
            _logger = logger;
            
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _profilesDirectory = Path.Combine(appDataPath, "PerunNetworkManager");
            _profilesFile = Path.Combine(_profilesDirectory, "profiles.json");
            _backupDirectory = Path.Combine(_profilesDirectory, "Backups");

            EnsureDirectoriesExist();
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(_profilesDirectory))
                Directory.CreateDirectory(_profilesDirectory);
                
            if (!Directory.Exists(_backupDirectory))
                Directory.CreateDirectory(_backupDirectory);
        }

        public async Task<List<NetworkProfile>> GetAllProfilesAsync()
        {
            try
            {
                if (!File.Exists(_profilesFile))
                {
                    _logger.LogInformation("Profiles file not found, creating new one");
                    return new List<NetworkProfile>();
                }

                var json = await File.ReadAllTextAsync(_profilesFile);
                var profiles = JsonConvert.DeserializeObject<List<NetworkProfile>>(json) ?? new List<NetworkProfile>();
                
                _logger.LogInformation($"Loaded {profiles.Count} profiles from {_profilesFile}");
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profiles");
                return new List<NetworkProfile>();
            }
        }

        public async Task<NetworkProfile?> GetProfileByIdAsync(Guid id)
        {
            var profiles = await GetAllProfilesAsync();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public async Task SaveProfileAsync(NetworkProfile profile)
        {
            try
            {
                profile.LastModified = DateTime.Now;
                
                var profiles = await GetAllProfilesAsync();
                var existingIndex = profiles.FindIndex(p => p.Id == profile.Id);
                
                if (existingIndex >= 0)
                {
                    profiles[existingIndex] = profile;
                    _logger.LogInformation($"Updated existing profile '{profile.Name}'");
                }
                else
                {
                    profiles.Add(profile);
                    _logger.LogInformation($"Added new profile '{profile.Name}'");
                }

                await SaveAllProfilesAsync(profiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving profile '{profile.Name}'");
                throw;
            }
        }

        public async Task SaveAllProfilesAsync(List<NetworkProfile> profiles)
        {
            try
            {
                // Create backup before saving
                await CreateBackupAsync();

                var json = JsonConvert.SerializeObject(profiles, Formatting.Indented, new JsonSerializerSettings
                {
                    DateFormatHandling = DateFormatHandling.IsoDateFormat,
                    NullValueHandling = NullValueHandling.Ignore
                });

                await File.WriteAllTextAsync(_profilesFile, json);
                _logger.LogInformation($"Saved {profiles.Count} profiles to {_profilesFile}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving all profiles");
                throw;
            }
        }

        public async Task DeleteProfileAsync(Guid id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profileToDelete = profiles.FirstOrDefault(p => p.Id == id);
                
                if (profileToDelete != null)
                {
                    profiles.Remove(profileToDelete);
                    await SaveAllProfilesAsync(profiles);
                    _logger.LogInformation($"Deleted profile '{profileToDelete.Name}'");
                }
                else
                {
                    _logger.LogWarning($"Profile with ID {id} not found for deletion");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting profile with ID {id}");
                throw;
            }
        }

        public async Task<List<NetworkProfile>> ImportProfilesAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                List<NetworkProfile> importedProfiles;

                switch (extension)
                {
                    case ".json":
                        importedProfiles = await ImportFromJsonAsync(filePath);
                        break;
                    case ".xml":
                        importedProfiles = await ImportFromXmlAsync(filePath);
                        break;
                    case ".npx":
                        importedProfiles = await ImportFromNpxAsync(filePath);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported file format: {extension}");
                }

                _logger.LogInformation($"Imported {importedProfiles.Count} profiles from {filePath}");
                return importedProfiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error importing profiles from {filePath}");
                throw;
            }
        }

        private async Task<List<NetworkProfile>> ImportFromJsonAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<List<NetworkProfile>>(json) ?? new List<NetworkProfile>();
        }

        private async Task<List<NetworkProfile>> ImportFromXmlAsync(string filePath)
        {
            var xml = XDocument.Load(filePath);
            var profiles = new List<NetworkProfile>();

            foreach (var profileElement in xml.Descendants("Profile"))
            {
                var profile = new NetworkProfile
                {
                    Id = Guid.Parse(profileElement.Attribute("Id")?.Value ?? Guid.NewGuid().ToString()),
                    Name = profileElement.Element("Name")?.Value ?? "Imported Profile",
                    Description = profileElement.Element("Description")?.Value ?? "",
                    UseDHCP = bool.Parse(profileElement.Element("UseDHCP")?.Value ?? "true"),
                    IPAddress = profileElement.Element("IPAddress")?.Value,
                    SubnetMask = profileElement.Element("SubnetMask")?.Value,
                    DefaultGateway = profileElement.Element("DefaultGateway")?.Value,
                    PrimaryDNS = profileElement.Element("PrimaryDNS")?.Value,
                    SecondaryDNS = profileElement.Element("SecondaryDNS")?.Value,
                    CreatedDate = DateTime.Parse(profileElement.Element("CreatedDate")?.Value ?? DateTime.Now.ToString()),
                    LastModified = DateTime.Now
                };

                profiles.Add(profile);
            }

            await Task.CompletedTask;
            return profiles;
        }

        private async Task<List<NetworkProfile>> ImportFromNpxAsync(string filePath)
        {
            // NPX is our custom encrypted format
            var encryptedData = await File.ReadAllBytesAsync(filePath);
            var decryptedJson = DecryptData(encryptedData);
            return JsonConvert.DeserializeObject<List<NetworkProfile>>(decryptedJson) ?? new List<NetworkProfile>();
        }

        public async Task ExportProfilesAsync(List<NetworkProfile> profiles, string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                switch (extension)
                {
                    case ".json":
                        await ExportToJsonAsync(profiles, filePath);
                        break;
                    case ".xml":
                        await ExportToXmlAsync(profiles, filePath);
                        break;
                    case ".npx":
                        await ExportToNpxAsync(profiles, filePath);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported export format: {extension}");
                }

                _logger.LogInformation($"Exported {profiles.Count} profiles to {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error exporting profiles to {filePath}");
                throw;
            }
        }

        private async Task ExportToJsonAsync(List<NetworkProfile> profiles, string filePath)
        {
            var json = JsonConvert.SerializeObject(profiles, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
        }

        private async Task ExportToXmlAsync(List<NetworkProfile> profiles, string filePath)
        {
            var xml = new XDocument(
                new XElement("NetworkProfiles",
                    new XAttribute("ExportDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XAttribute("Version", "1.0"),
                    profiles.Select(profile => new XElement("Profile",
                        new XAttribute("Id", profile.Id),
                        new XElement("IPAddress", profile.IPAddress ?? ""),
                        new XElement("SubnetMask", profile.SubnetMask ?? ""),
                        new XElement("DefaultGateway", profile.DefaultGateway ?? ""),
                        new XElement("PrimaryDNS", profile.PrimaryDNS ?? ""),
                        new XElement("SecondaryDNS", profile.SecondaryDNS ?? ""),
                        new XElement("PrimaryWINS", profile.PrimaryWINS ?? ""),
                        new XElement("SecondaryWINS", profile.SecondaryWINS ?? ""),
                        new XElement("ComputerName", profile.ComputerName ?? ""),
                        new XElement("WorkgroupDomain", profile.WorkgroupDomain ?? ""),
                        new XElement("IsDomain", profile.IsDomain),
                        new XElement("CreatedDate", profile.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")),
                        new XElement("LastModified", profile.LastModified.ToString("yyyy-MM-dd HH:mm:ss")),
                        new XElement("Icon", profile.Icon.ToString()),
                        new XElement("Notes", profile.Notes ?? "")
                    ))
                )
            );

            xml.Save(filePath);
            await Task.CompletedTask;
        }

        private async Task ExportToNpxAsync(List<NetworkProfile> profiles, string filePath)
        {
            var json = JsonConvert.SerializeObject(profiles, Formatting.Indented);
            var encryptedData = EncryptData(json);
            await File.WriteAllBytesAsync(filePath, encryptedData);
        }

        private byte[] EncryptData(string data)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes("PerunNetworkManager1234567890123456"[..32]); // 32 bytes for AES-256
                aes.IV = Encoding.UTF8.GetBytes("1234567890123456"); // 16 bytes for IV

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using var swEncrypt = new StreamWriter(csEncrypt);
                
                swEncrypt.Write(data);
                return msEncrypt.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encrypting data");
                throw;
            }
        }

        private string DecryptData(byte[] encryptedData)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes("PerunNetworkManager1234567890123456"[..32]); // 32 bytes for AES-256
                aes.IV = Encoding.UTF8.GetBytes("1234567890123456"); // 16 bytes for IV

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(encryptedData);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                
                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting data");
                throw;
            }
        }

        public async Task<NetworkConfiguration> GetCurrentNetworkConfigurationAsync()
        {
            try
            {
                var config = new NetworkConfiguration
                {
                    BackupDate = DateTime.Now
                };

                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                               ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                foreach (var ni in networkInterfaces)
                {
                    var ipProperties = ni.GetIPProperties();
                    
                    // Get IP addresses
                    var ipAddresses = ipProperties.UnicastAddresses
                        .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(ua => new IPConfiguration
                        {
                            Address = ua.Address.ToString(),
                            SubnetMask = ua.IPv4Mask?.ToString() ?? ""
                        })
                        .ToList();

                    if (ipAddresses.Any())
                    {
                        config.AdapterName = ni.Name;
                        config.IPAddresses = ipAddresses;
                        
                        // Get gateways
                        config.Gateways = ipProperties.GatewayAddresses
                            .Where(ga => ga.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            .Select(ga => ga.Address.ToString())
                            .ToList();

                        // Get DNS servers
                        config.DnsServers = ipProperties.DnsAddresses
                            .Where(dns => dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            .Select(dns => dns.ToString())
                            .ToList();
                        
                        break; // Use the first active adapter
                    }
                }

                await Task.CompletedTask;
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current network configuration");
                throw;
            }
        }

        public bool ProfileMatchesConfiguration(NetworkProfile profile, NetworkConfiguration config)
        {
            try
            {
                if (profile.UseDHCP)
                {
                    // For DHCP profiles, just check if there's an IP address assigned
                    return config.IPAddresses.Any();
                }
                else
                {
                    // For static profiles, check IP, subnet mask, and gateway
                    var hasMatchingIP = config.IPAddresses.Any(ip => ip.Address == profile.IPAddress);
                    var hasMatchingSubnet = config.IPAddresses.Any(ip => ip.SubnetMask == profile.SubnetMask);
                    var hasMatchingGateway = string.IsNullOrEmpty(profile.DefaultGateway) || 
                                           config.Gateways.Contains(profile.DefaultGateway);
                    
                    return hasMatchingIP && hasMatchingSubnet && hasMatchingGateway;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error matching profile to configuration");
                return false;
            }
        }

        public async Task CreateBackupAsync()
        {
            try
            {
                if (!File.Exists(_profilesFile))
                    return;

                var backupFileName = $"profiles_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var backupPath = Path.Combine(_backupDirectory, backupFileName);
                
                await File.CopyToAsync(_profilesFile, backupPath);
                
                // Clean up old backups (keep last 10)
                await CleanupOldBackupsAsync();
                
                _logger.LogInformation($"Created backup at {backupPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating backup");
            }
        }

        private async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupDirectory, "profiles_backup_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(10) // Keep last 10 backups
                    .ToList();

                foreach (var file in backupFiles)
                {
                    File.Delete(file);
                }

                if (backupFiles.Any())
                {
                    _logger.LogInformation($"Cleaned up {backupFiles.Count} old backup files");
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up old backups");
            }
        }

        public async Task<List<NetworkProfile>> GetRecentProfilesAsync(int count = 5)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                return profiles
                    .OrderByDescending(p => p.LastModified)
                    .Take(count)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent profiles");
                return new List<NetworkProfile>();
            }
        }

        public async Task<List<NetworkProfile>> GetProfilesByAdapterAsync(string adapterName)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                return profiles
                    .Where(p => string.IsNullOrEmpty(p.AdapterName) || p.AdapterName.Equals(adapterName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting profiles for adapter '{adapterName}'");
                return new List<NetworkProfile>();
            }
        }

        public async Task<bool> ValidateProfileAsync(NetworkProfile profile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                    return false;

                if (!profile.UseDHCP)
                {
                    // Validate static IP configuration
                    if (!IPAddress.TryParse(profile.IPAddress, out _))
                        return false;
                        
                    if (!IPAddress.TryParse(profile.SubnetMask, out _))
                        return false;
                        
                    if (!string.IsNullOrEmpty(profile.DefaultGateway) && !IPAddress.TryParse(profile.DefaultGateway, out _))
                        return false;
                }

                // Validate DNS servers
                if (!string.IsNullOrEmpty(profile.PrimaryDNS) && !IPAddress.TryParse(profile.PrimaryDNS, out _))
                    return false;
                    
                if (!string.IsNullOrEmpty(profile.SecondaryDNS) && !IPAddress.TryParse(profile.SecondaryDNS, out _))
                    return false;

                // Validate WINS servers
                if (!string.IsNullOrEmpty(profile.PrimaryWINS) && !IPAddress.TryParse(profile.PrimaryWINS, out _))
                    return false;
                    
                if (!string.IsNullOrEmpty(profile.SecondaryWINS) && !IPAddress.TryParse(profile.SecondaryWINS, out _))
                    return false;

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating profile '{profile.Name}'");
                return false;
            }
        }

        public async Task<NetworkProfile> CloneProfileAsync(NetworkProfile originalProfile, string newName)
        {
            try
            {
                var clonedProfile = originalProfile.Clone();
                clonedProfile.Name = newName;
                clonedProfile.IsActive = false;
                
                await SaveProfileAsync(clonedProfile);
                _logger.LogInformation($"Cloned profile '{originalProfile.Name}' as '{newName}'");
                
                return clonedProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cloning profile '{originalProfile.Name}'");
                throw;
            }
        }

        public async Task<bool> ExistsProfileWithNameAsync(string name, Guid? excludeId = null)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                return profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && 
                                        (!excludeId.HasValue || p.Id != excludeId.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if profile name '{name}' exists");
                return false;
            }
        }

        public async Task<string> GenerateUniqueProfileNameAsync(string baseName)
        {
            try
            {
                var name = baseName;
                var counter = 1;
                
                while (await ExistsProfileWithNameAsync(name))
                {
                    name = $"{baseName} ({counter})";
                    counter++;
                }
                
                return name;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating unique profile name for '{baseName}'");
                return $"{baseName}_{Guid.NewGuid().ToString()[..8]}";
            }
        }

        public async Task<Dictionary<string, object>> GetProfileStatisticsAsync()
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var stats = new Dictionary<string, object>
                {
                    ["TotalProfiles"] = profiles.Count,
                    ["DHCPProfiles"] = profiles.Count(p => p.UseDHCP),
                    ["StaticProfiles"] = profiles.Count(p => !p.UseDHCP),
                    ["ActiveProfiles"] = profiles.Count(p => p.IsActive),
                    ["ProfilesWithScripts"] = profiles.Count(p => p.Scripts.Any()),
                    ["ProfilesWithProxy"] = profiles.Count(p => p.Proxy.UseProxy),
                    ["ProfilesWithPrinters"] = profiles.Count(p => p.Printers.Any()),
                    ["MostRecentProfile"] = profiles.OrderByDescending(p => p.LastModified).FirstOrDefault()?.Name ?? "None",
                    ["OldestProfile"] = profiles.OrderBy(p => p.CreatedDate).FirstOrDefault()?.Name ?? "None"
                };
                
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile statistics");
                return new Dictionary<string, object>();
            }
        }
    }

    public static class FileExtensions
    {
        public static async Task CopyToAsync(string sourcePath, string destinationPath)
        {
            using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await sourceStream.CopyToAsync(destinationStream);
        }
    }
}Name", profile.Name),
                        new XElement("Description", profile.Description),
                        new XElement("UseDHCP", profile.UseDHCP),
                        new XElement("
