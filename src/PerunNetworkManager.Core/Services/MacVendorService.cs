using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PerunNetworkManager.Core.Services
{
    public class MacVendorService
    {
        private readonly ILogger<MacVendorService> _logger;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> _vendorCache;
        private readonly Dictionary<string, string> _ouiDatabase;
        private readonly SemaphoreSlim _cacheSemaphore;
        private DateTime _lastCacheCleanup;
        private const int MaxCacheSize = 10000;
        private const int CacheCleanupIntervalHours = 24;

        public MacVendorService(ILogger<MacVendorService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Perun-Network-Manager/1.0");
            
            _vendorCache = new Dictionary<string, string>();
            _ouiDatabase = new Dictionary<string, string>();
            _cacheSemaphore = new SemaphoreSlim(1, 1);
            _lastCacheCleanup = DateTime.Now;
            
            InitializeBuiltInOuiDatabase();
        }

        public async Task<string> GetVendorAsync(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                return "Unknown";

            try
            {
                var oui = ExtractOui(macAddress);
                if (string.IsNullOrEmpty(oui))
                    return "Unknown";

                // Check cache first
                await _cacheSemaphore.WaitAsync();
                try
                {
                    if (_vendorCache.TryGetValue(oui, out var cachedVendor))
                    {
                        return cachedVendor;
                    }
                }
                finally
                {
                    _cacheSemaphore.Release();
                }

                // Check built-in OUI database
                if (_ouiDatabase.TryGetValue(oui, out var builtInVendor))
                {
                    await CacheVendorAsync(oui, builtInVendor);
                    return builtInVendor;
                }

                // Try online lookup
                var onlineVendor = await LookupVendorOnlineAsync(oui);
                if (!string.IsNullOrEmpty(onlineVendor))
                {
                    await CacheVendorAsync(oui, onlineVendor);
                    return onlineVendor;
                }

                // Fallback to unknown
                await CacheVendorAsync(oui, "Unknown");
                return "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error looking up vendor for MAC address: {MacAddress}", macAddress);
                return "Unknown";
            }
        }

        private string ExtractOui(string macAddress)
        {
            try
            {
                // Remove common separators and get first 6 characters (3 bytes)
                var cleanMac = macAddress.Replace(":", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
                
                if (cleanMac.Length >= 6)
                {
                    return cleanMac.Substring(0, 6);
                }
                
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<string> LookupVendorOnlineAsync(string oui)
        {
            try
            {
                // Try multiple online services with fallback
                var services = new[]
                {
                    $"https://api.macvendors.com/{oui}",
                    $"https://macvendors.co/api/{oui}",
                    $"https://www.macvendorlookup.com/api/v2/{oui}"
                };

                foreach (var serviceUrl in services)
                {
                    try
                    {
                        var response = await _httpClient.GetAsync(serviceUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var vendor = ParseVendorResponse(content, serviceUrl);
                            
                            if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown")
                            {
                                _logger.LogDebug("Found vendor '{Vendor}' for OUI {OUI} from {Service}", vendor, oui, serviceUrl);
                                return vendor;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to lookup vendor from {Service} for OUI {OUI}", serviceUrl, oui);
                        continue;
                    }

                    // Rate limiting - small delay between requests
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Online vendor lookup failed for OUI: {OUI}", oui);
            }

            return string.Empty;
        }

        private string ParseVendorResponse(string content, string serviceUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                    return string.Empty;

                // Handle different API response formats
                if (serviceUrl.Contains("macvendors.com"))
                {
                    // macvendors.com returns plain text
                    return content.Trim();
                }
                else if (serviceUrl.Contains("macvendors.co"))
                {
                    // macvendors.co returns JSON
                    var jsonDoc = JsonDocument.Parse(content);
                    if (jsonDoc.RootElement.TryGetProperty("result", out var result))
                    {
                        if (result.TryGetProperty("company", out var company))
                        {
                            return company.GetString() ?? string.Empty;
                        }
                    }
                }
                else if (serviceUrl.Contains("macvendorlookup.com"))
                {
                    // macvendorlookup.com returns JSON array
                    var jsonArray = JsonDocument.Parse(content);
                    if (jsonArray.RootElement.ValueKind == JsonValueKind.Array && jsonArray.RootElement.GetArrayLength() > 0)
                    {
                        var firstItem = jsonArray.RootElement[0];
                        if (firstItem.TryGetProperty("company", out var company))
                        {
                            return company.GetString() ?? string.Empty;
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error parsing vendor response from {Service}: {Content}", serviceUrl, content);
                return string.Empty;
            }
        }

        private async Task CacheVendorAsync(string oui, string vendor)
        {
            try
            {
                await _cacheSemaphore.WaitAsync();
                try
                {
                    _vendorCache[oui] = vendor;
                    
                    // Periodic cache cleanup
                    if (DateTime.Now.Subtract(_lastCacheCleanup).TotalHours > CacheCleanupIntervalHours)
                    {
                        await CleanupCacheAsync();
                    }
                }
                finally
                {
                    _cacheSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error caching vendor for OUI: {OUI}", oui);
            }
        }

        private async Task CleanupCacheAsync()
        {
            try
            {
                if (_vendorCache.Count > MaxCacheSize)
                {
                    // Remove oldest entries (simple FIFO cleanup)
                    var keysToRemove = _vendorCache.Keys.Take(_vendorCache.Count - MaxCacheSize + 1000).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _vendorCache.Remove(key);
                    }
                    
                    _logger.LogDebug("Cleaned up vendor cache, removed {Count} entries", keysToRemove.Count);
                }
                
                _lastCacheCleanup = DateTime.Now;
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during cache cleanup");
            }
        }

        private void InitializeBuiltInOuiDatabase()
        {
            // Initialize with most common vendor OUIs for offline lookup
            var commonOuis = new Dictionary<string, string>
            {
                // Apple
                { "001124", "Apple" }, { "001451", "Apple" }, { "0016CB", "Apple" }, { "001EC2", "Apple" },
                { "001F5B", "Apple" }, { "002241", "Apple" }, { "002332", "Apple" }, { "002436", "Apple" },
                { "0025BC", "Apple" }, { "002608", "Apple" }, { "0026BB", "Apple" }, { "002819", "Apple" },
                { "003065", "Apple" }, { "003EE1", "Apple" }, { "0050E4", "Apple" }, { "006171", "Apple" },
                { "0C1420", "Apple" }, { "0C4DE9", "Apple" }, { "0C74C2", "Apple" }, { "101C0C", "Apple" },
                { "14109F", "Apple" }, { "1499E2", "Apple" }, { "185E0F", "Apple" }, { "1C91AC", "Apple" },
                { "20A2E4", "Apple" }, { "24F094", "Apple" }, { "286AB8", "Apple" }, { "28CFE9", "Apple" },
                { "2CF0EE", "Apple" }, { "30F7C5", "Apple" }, { "34159E", "Apple" }, { "34A395", "Apple" },
                { "380195", "Apple" }, { "3C0754", "Apple" }, { "40CBC0", "Apple" }, { "44FB42", "Apple" },
                { "48A91C", "Apple" }, { "4C57CA", "Apple" }, { "4C8D79", "Apple" }, { "5056F3", "Apple" },
                { "545C94", "Apple" }, { "58B035", "Apple" }, { "5C70A3", "Apple" }, { "5CF7E6", "Apple" },
                { "60334B", "Apple" }, { "609AC1", "Apple" }, { "64200C", "Apple" }, { "64E682", "Apple" },
                { "68AE20", "Apple" }, { "6C4008", "Apple" }, { "6C709F", "Apple" }, { "6CAB31", "Apple" },
                { "70CD60", "Apple" }, { "7073CB", "Apple" }, { "78D75F", "Apple" }, { "7CD1C3", "Apple" },
                { "80E650", "Apple" }, { "8425DB", "Apple" }, { "843835", "Apple" }, { "8863DF", "Apple" },
                { "8C8EF2", "Apple" }, { "90840D", "Apple" }, { "908D6C", "Apple" }, { "9027E4", "Apple" },
                { "907240", "Apple" }, { "94E96A", "Apple" }, { "9803D8", "Apple" }, { "9C35EB", "Apple" },
                { "A04EA7", "Apple" }, { "A85C2C", "Apple" }, { "A8667F", "Apple" }, { "A8FAD8", "Apple" },
                { "AC3743", "Apple" }, { "AC87A3", "Apple" }, { "B09FBA", "Apple" }, { "B418D1", "Apple" },
                { "B8782E", "Apple" }, { "B8C75A", "Apple" }, { "BC926B", "Apple" }, { "C02F2D", "Apple" },
                { "C08997", "Apple" }, { "C42C03", "Apple" }, { "C869CD", "Apple" }, { "CC08E0", "Apple" },
                { "D0E140", "Apple" }, { "D49A20", "Apple" }, { "D8004D", "Apple" }, { "D8A25E", "Apple" },
                { "DC2B2A", "Apple" }, { "DC56E7", "Apple" }, { "E0ACCB", "Apple" }, { "E425E7", "Apple" },
                { "E80688", "Apple" }, { "E8802E", "Apple" }, { "EC8350", "Apple" }, { "F025B7", "Apple" },
                { "F0B479", "Apple" }, { "F0C1F1", "Apple" }, { "F0DBF8", "Apple" }, { "F4F15A", "Apple" },
                { "F82793", "Apple" }, { "FC253F", "Apple" },

                // Microsoft
                { "000D3A", "Microsoft" }, { "0050F2", "Microsoft" }, { "001DD8", "Microsoft" },
                { "0017FA", "Microsoft" }, { "002556", "Microsoft" }, { "7C1E52", "Microsoft" },
                { "009027", "Microsoft" }, { "00155D", "Microsoft" }, { "A0999B", "Microsoft" },

                // Samsung
                { "002454", "Samsung" }, { "0024E9", "Samsung" }, { "00264A", "Samsung" },
                { "001485", "Samsung" }, { "001377", "Samsung" }, { "000E8F", "Samsung" },
                { "8806BF", "Samsung" }, { "8C77DC", "Samsung" }, { "BC44AA", "Samsung" },
                { "E4B021", "Samsung" }, { "34AA99", "Samsung" }, { "5C0A5B", "Samsung" },

                // Intel
                { "001B63", "Intel" }, { "0050E4", "Intel" }, { "00A0C9", "Intel" },
                { "001111", "Intel" }, { "3497F6", "Intel" }, { "7085C2", "Intel" },
                { "A0A8CD", "Intel" }, { "B479A7", "Intel" }, { "00E04C", "Intel" },

                // Cisco
                { "000142", "Cisco" }, { "000163", "Cisco" }, { "00016C", "Cisco" },
                { "000195", "Cisco" }, { "0001C9", "Cisco" }, { "0001C7", "Cisco" },
                { "0002FD", "Cisco" }, { "000318", "Cisco" }, { "000356", "Cisco" },
                { "0003E3", "Cisco" }, { "0003FD", "Cisco" }, { "000423", "Cisco" },
                { "000476", "Cisco" }, { "0004DD", "Cisco" }, { "000502", "Cisco" },
                { "000781", "Cisco" }, { "0007B3", "Cisco" }, { "0007EB", "Cisco" },
                { "000854", "Cisco" }, { "000906", "Cisco" }, { "000947", "Cisco" },
                { "000A41", "Cisco" }, { "000A42", "Cisco" }, { "000A8A", "Cisco" },
                { "000AA5", "Cisco" }, { "000B45", "Cisco" }, { "000B46", "Cisco" },
                { "000B5F", "Cisco" }, { "000B85", "Cisco" }, { "000BBA", "Cisco" },
                { "000BBE", "Cisco" }, { "000BBF", "Cisco" }, { "000C30", "Cisco" },
                { "000C41", "Cisco" }, { "000C85", "Cisco" }, { "000C86", "Cisco" },
                { "000CDB", "Cisco" }, { "000CEC", "Cisco" }, { "000D28", "Cisco" },
                { "000D29", "Cisco" }, { "000D65", "Cisco" }, { "000DBC", "Cisco" },
                { "000DBD", "Cisco" }, { "000DCB", "Cisco" }, { "000E08", "Cisco" },
                { "000E38", "Cisco" }, { "000E39", "Cisco" }, { "000E83", "Cisco" },
                { "000E84", "Cisco" }, { "000ED7", "Cisco" }, { "000F23", "Cisco" },
                { "000F24", "Cisco" }, { "000F34", "Cisco" }, { "000F35", "Cisco" },
                { "000F66", "Cisco" }, { "000F8F", "Cisco" }, { "000F90", "Cisco" },

                // HP/Hewlett-Packard
                { "001279", "HP" }, { "001321", "HP" }, { "001438", "HP" },
                { "001560", "HP" }, { "0016B9", "HP" }, { "001708", "HP" },
                { "0017A4", "HP" }, { "00188B", "HP" }, { "001A4B", "HP" },
                { "002170", "HP" }, { "00236C", "HP" }, { "002481", "HP" },
                { "0026B9", "HP" }, { "00306E", "HP" }, { "080009", "HP" },

                // Dell
                { "000874", "Dell" }, { "000B3B", "Dell" }, { "000C29", "Dell" },
                { "000D56", "Dell" }, { "000E0C", "Dell" }, { "000F1F", "Dell" },
                { "001143", "Dell" }, { "001344", "Dell" }, { "0014D1", "Dell" },
                { "001560", "Dell" }, { "0017A4", "Dell" }, { "001AA0", "Dell" },
                { "002170", "Dell" }, { "002564", "Dell" }, { "0026B9", "Dell" },

                // ASUS
                { "000C6E", "ASUS" }, { "000EA6", "ASUS" }, { "0015F2", "ASUS" },
                { "001731", "ASUS" }, { "001B11", "ASUS" }, { "001E8C", "ASUS" },
                { "002215", "ASUS" }, { "0025D3", "ASUS" }, { "107B44", "ASUS" },
                { "20CF30", "ASUS" }, { "2C56DC", "ASUS" }, { "30ABA5", "ASUS" },
                { "38D547", "ASUS" }, { "40E230", "ASUS" }, { "50465D", "ASUS" },
                { "54BF64", "ASUS" }, { "60A44C", "ASUS" }, { "704D7B", "ASUS" },
                { "74D435", "ASUS" }, { "7C2664", "ASUS" }, { "88D7F6", "ASUS" },
                { "9C5C8E", "ASUS" }, { "AC9E17", "ASUS" }, { "B06EBF", "ASUS" },
                { "BC6397", "ASUS" }, { "BC7670", "ASUS" }, { "F832E4", "ASUS" },

                // D-Link
                { "001195", "D-Link" }, { "0013469", "D-Link" }, { "001346", "D-Link" },
                { "001CF0", "D-Link" }, { "002191", "D-Link" }, { "0022B0", "D-Link" },
                { "00265A", "D-Link" }, { "141DD2", "D-Link" }, { "1C7EE5", "D-Link" },
                { "1CAFF7", "D-Link" }, { "340804", "D-Link" }, { "5CD998", "D-Link" },

                // TP-Link
                { "001B2F", "TP-Link" }, { "001E58", "TP-Link" }, { "002268", "TP-Link" },
                { "0025BC", "TP-Link" }, { "04C066", "TP-Link" }, { "0C80DA", "TP-Link" },
                { "149558", "TP-Link" }, { "1C61B4", "TP-Link" }, { "30B5C2", "TP-Link" },
                { "3872C0", "TP-Link" }, { "50C7BF", "TP-Link" }, { "64B6F7", "TP-Link" },
                { "A42BB0", "TP-Link" }, { "B076BE", "TP-Link" }, { "C006C3", "TP-Link" },
                { "C44ADB", "TP-Link" }, { "D850E6", "TP-Link" }, { "E8DE27", "TP-Link" },
                { "EC086B", "TP-Link" }, { "F09FC2", "TP-Link" }, { "F46D04", "TP-Link" },

                // Netgear
                { "001B2F", "Netgear" }, { "0024B2", "Netgear" }, { "002713", "Netgear" },
                { "003048", "Netgear" }, { "0846D6", "Netgear" }, { "10BF48", "Netgear" },
                { "20E52A", "Netgear" }, { "2C3033", "Netgear" }, { "30469A", "Netgear" },
                { "44944A", "Netgear" }, { "6038E0", "Netgear" }, { "74440E", "Netgear" },
                { "84C9B2", "Netgear" }, { "9C3426", "Netgear" }, { "A040A0", "Netg
