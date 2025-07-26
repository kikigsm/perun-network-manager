# 🌐 Perun Network Manager

<div align="center">

![Perun Network Manager](https://raw.githubusercontent.com/kikigsm/perun-network-manager/main/docs/assets/perun-logo.png)

**Advanced Network Profile Manager with Subnet Scanner**

[![Build Status](https://github.com/kikigsm/perun-network-manager/workflows/Build/badge.svg)](https://github.com/kikigsm/perun-network-manager/actions)
[![Release](https://img.shields.io/github/v/release/kikigsm/perun-network-manager)](https://github.com/kikigsm/perun-network-manager/releases)
[![License](https://img.shields.io/github/license/kikigsm/perun-network-manager)](LICENSE)
[![Downloads](https://img.shields.io/github/downloads/kikigsm/perun-network-manager/total)](https://github.com/kikigsm/perun-network-manager/releases)
[![Stars](https://img.shields.io/github/stars/kikigsm/perun-network-manager)](https://github.com/kikigsm/perun-network-manager/stargazers)

*Professional network management tool for Windows with advanced subnet scanning capabilities*

</div>

## ✨ Features

- 🔧 **Advanced Network Profile Management** - Create unlimited network profiles with complete IP configuration
- 📡 **Multi-threaded Subnet Scanner** - Discover devices with MAC address, vendor, and service identification  
- 🎯 **Device Discovery & Monitoring** - Real-time network mapping with device categorization
- 🔒 **Enterprise Security** - Administrator privilege handling and encrypted profile storage
- 🎨 **Modern Material Design UI** - Professional interface with dark/light theme support
- 🌐 **Multi-language Support** - 60+ languages including Serbian (Cyrillic/Latin)
- ⚡ **Wake-on-LAN** - Remote device wake capabilities
- 📊 **Network Diagnostics** - Comprehensive testing and troubleshooting tools
- 📋 **Export/Import** - Multiple formats (JSON, XML, encrypted NPX)
- 🔧 **System Tray Integration** - Quick profile switching and background operation
- 🏢 **Enterprise Ready** - WiX installer with Group Policy support

## 🚀 Quick Start

### Prerequisites

- **Windows 10/11** (Administrator privileges required)
- **.NET 6.0 Desktop Runtime** ([Download here](https://dotnet.microsoft.com/download/dotnet/6.0))
- **200 MB** disk space minimum

### Installation

1. **Download** the latest release from [Releases](https://github.com/kikigsm/perun-network-manager/releases)
2. **Run** the MSI installer as Administrator
3. **Launch** from Start Menu or Desktop shortcut

### First Use

1. **Create Profile** - Add your first network profile
2. **Configure Settings** - Set IP configuration (DHCP or Static)
3. **Apply Profile** - Activate the profile on your network adapter
4. **Scan Network** - Use the Network Scanner to discover devices

## 📖 Documentation

- [📘 User Guide](https://github.com/kikigsm/perun-network-manager/wiki/User-Guide) - Complete usage instructions
- [🔧 Installation Guide](https://github.com/kikigsm/perun-network-manager/wiki/Installation) - Detailed setup procedures  
- [👨‍💻 Development Guide](https://github.com/kikigsm/perun-network-manager/wiki/Development) - Building from source
- [📚 API Reference](https://github.com/kikigsm/perun-network-manager/wiki/API-Reference) - Developer documentation
- [🛠️ Troubleshooting](https://github.com/kikigsm/perun-network-manager/wiki/Troubleshooting) - Common issues and solutions

## 🔥 Key Capabilities

### Network Profile Management
- **Unlimited Profiles** with custom naming and descriptions
- **Complete IP Configuration** (Static IP, DHCP, DNS, WINS, Gateway)
- **Domain/Workgroup Settings** with credential management
- **Printer Configuration** with automatic driver installation
- **Proxy Settings** with authentication support
- **Custom Scripts** (PowerShell/Batch) with trigger events

### Advanced Network Scanner
- **Multi-threaded Scanning** with configurable thread pools
- **Device Identification** using MAC address vendor lookup
- **Service Discovery** through port scanning and banner grabbing
- **Operating System Detection** via TTL analysis and fingerprinting
- **Network Topology Mapping** with visual device representation
- **Real-time Monitoring** with change notifications

### Enterprise Features
- **Administrator Privilege Handling** with UAC integration
- **Encrypted Profile Storage** using AES-256 encryption
- **Group Policy Support** for centralized deployment
- **Audit Logging** with comprehensive activity tracking
- **Silent Installation** with MSI deployment options
- **Multi-language Interface** with 60+ supported languages

## 🛠️ Development

### Building from Source

```bash
# Clone repository
git clone https://github.com/kikigsm/perun-network-manager.git
cd perun-network-manager

# Restore dependencies
dotnet restore

# Build solution
dotnet build --configuration Release

# Run tests
dotnet test

# Create installer (requires WiX Toolset v6.0+)
dotnet build src/PerunNetworkManager.Installer/ --configuration Release
```

### Development Requirements

- **Visual Studio 2022** (17.12+) or VS Code with C# extension
- **.NET 6.0 SDK** or later
- **WiX Toolset v6.0+** for installer creation
- **Git LFS** for binary assets

### Technology Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Framework** | .NET 6 WPF | Application foundation |
| **UI Framework** | Material Design in XAML | Modern interface design |
| **Architecture** | MVVM Community Toolkit | Clean separation of concerns |
| **Network Access** | System.Management (WMI) | Windows network integration |
| **Scanning Engine** | Multi-threaded .NET | High-performance device discovery |
| **Installer** | WiX Toolset v6 | Professional MSI creation |
| **Logging** | Microsoft.Extensions.Logging | Comprehensive diagnostics |

## 🤝 Contributing

We welcome contributions from the community! Here's how to get started:

### Quick Contribution Guide

1. **Fork** the repository
2. **Create** your feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

### Contribution Areas

- 🐛 **Bug Fixes** - Help improve stability and reliability
- ✨ **Feature Development** - Add new network management capabilities
- 🌐 **Translations** - Support for additional languages
- 📖 **Documentation** - Improve guides and API documentation
- 🧪 **Testing** - Expand test coverage and scenarios
- 🎨 **UI/UX** - Enhance user interface and experience

For detailed guidelines, see [CONTRIBUTING.md](https://github.com/kikigsm/perun-network-manager/blob/main/CONTRIBUTING.md)

## 📝 License

This project is licensed under the **MIT License** - see the [LICENSE](https://github.com/kikigsm/perun-network-manager/blob/main/LICENSE) file for details.

## 🙏 Acknowledgments

- **Material Design Team** - For the beautiful and intuitive design system
- **Microsoft .NET Team** - For the excellent development platform
- **WiX Toolset Contributors** - For professional installer capabilities
- **Community Contributors** - For testing, feedback, and improvements
- **Open Source Community** - For inspiration and best practices

## 📊 Project Stats

- **Languages**: C#, XAML, PowerShell
- **Platforms**: Windows 10/11
- **Architecture**: x64, x86
- **Installer Size**: ~15 MB
- **Runtime Memory**: ~50-100 MB
- **Supported Networks**: IPv4, Ethernet, WiFi, VPN

## 📞 Support & Community

### Get Help
- 🐛 [Report Bugs](https://github.com/kikigsm/perun-network-manager/issues/new?template=bug_report.md)
- 💡 [Request Features](https://github.com/kikigsm/perun-network-manager/issues/new?template=feature_request.md)
- ❓ [Ask Questions](https://github.com/kikigsm/perun-network-manager/discussions)
- 📖 [Read Documentation](https://github.com/kikigsm/perun-network-manager/wiki)

### Stay Updated
- ⭐ **Star** this repository to show support
- 👀 **Watch** for updates and new releases
- 🔔 Enable **notifications** for important announcements

## 🚀 Roadmap

### Version 1.1 (Planned)
- [ ] IPv6 support and dual-stack configuration
- [ ] Network performance monitoring and graphing
- [ ] Advanced VPN profile management
- [ ] RESTful API for automation and integration
- [ ] PowerShell module for command-line operations

### Version 1.2 (Future)
- [ ] Cloud profile synchronization
- [ ] Network security scanning capabilities
- [ ] Integration with Active Directory
- [ ] Mobile companion app for monitoring
- [ ] Advanced reporting and analytics

---

<div align="center">

**Made with ❤️ by [kikigsm](https://github.com/kikigsm)**

*Perun Network Manager - Professional Network Management for Everyone*

[⬆ Back to Top](#-perun-network-manager)

</div>
