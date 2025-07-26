# Contributing to Perun Network Manager

Thank you for your interest in contributing to Perun Network Manager! We welcome contributions from the community and are excited to see what you'll bring to the project.

## 🚀 Quick Start

1. **Fork** the repository on GitHub
2. **Clone** your fork locally
3. **Create** a feature branch
4. **Make** your changes
5. **Test** your changes thoroughly
6. **Submit** a pull request

## 📋 Table of Contents

- [Code of Conduct](#-code-of-conduct)
- [Getting Started](#-getting-started)
- [Development Environment](#-development-environment)
- [Making Changes](#-making-changes)
- [Testing Guidelines](#-testing-guidelines)
- [Submitting Changes](#-submitting-changes)
- [Code Style](#-code-style)
- [Documentation](#-documentation)
- [Community](#-community)

## 📜 Code of Conduct

This project adheres to a [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to [kikigsm@github.com](mailto:kikigsm@github.com).

## 🛠️ Getting Started

### Prerequisites

- **Windows 10/11** (for full testing)
- **Visual Studio 2022** (17.12+) or VS Code with C# extension
- **.NET 6.0 SDK** or later
- **Git** for version control
- **WiX Toolset v6.0+** (for installer development)

### Setting Up Your Development Environment

1. **Fork the repository**
   ```bash
   # Navigate to https://github.com/kikigsm/perun-network-manager
   # Click "Fork" to create your own copy
   ```

2. **Clone your fork**
   ```bash
   git clone https://github.com/YOUR_USERNAME/perun-network-manager.git
   cd perun-network-manager
   ```

3. **Add upstream remote**
   ```bash
   git remote add upstream https://github.com/kikigsm/perun-network-manager.git
   ```

4. **Restore dependencies**
   ```bash
   dotnet restore
   ```

5. **Build the solution**
   ```bash
   dotnet build --configuration Debug
   ```

6. **Run tests**
   ```bash
   dotnet test
   ```

## 💻 Development Environment

### Recommended Tools

- **Visual Studio 2022** with the following workloads:
  - .NET desktop development
  - Universal Windows Platform development (optional)
- **Visual Studio Extensions**:
  - WiX Toolset Visual Studio Extension
  - SonarLint for Visual Studio
  - CodeMaid (for code cleanup)

### Project Structure

```
src/
├── PerunNetworkManager/          # Main WPF application
├── PerunNetworkManager.Core/     # Core business logic
├── PerunNetworkManager.Tests/    # Unit and integration tests
└── PerunNetworkManager.Installer/ # WiX installer project
```

## 🔄 Making Changes

### Branching Strategy

We use **Git Flow** with the following branch types:

- `main` - Production-ready code
- `develop` - Integration branch for features
- `feature/*` - New features
- `bugfix/*` - Bug fixes
- `hotfix/*` - Critical production fixes
- `release/*` - Release preparation

### Creating a Feature Branch

```bash
# Start from develop branch
git checkout develop
git pull upstream develop

# Create feature branch
git checkout -b feature/your-feature-name

# Make your changes
# ...

# Commit your changes
git add .
git commit -m "feat: add your feature description"

# Push to your fork
git push origin feature/your-feature-name
```

### Commit Message Convention

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**Types:**
- `feat` - New features
- `fix` - Bug fixes
- `docs` - Documentation changes
- `style` - Code style changes (formatting, etc.)
- `refactor` - Code refactoring
- `test` - Adding or updating tests
- `chore` - Maintenance tasks

**Examples:**
```
feat(scanner): add device vendor identification
fix(ui): resolve theme switching issue in main window
docs: update installation guide with new prerequisites
test(core): add unit tests for profile service
```

## 🧪 Testing Guidelines

### Test Categories

1. **Unit Tests** - Test individual components in isolation
2. **Integration Tests** - Test component interactions
3. **UI Tests** - Test user interface functionality
4. **Network Tests** - Test network-dependent features (require admin privileges)

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Writing Tests

- **Use descriptive test names** that explain what is being tested
- **Follow AAA pattern** (Arrange, Act, Assert)
- **Mock external dependencies** using Moq or similar frameworks
- **Test both success and failure scenarios**
- **Include edge cases and boundary conditions**

Example:
```csharp
[Test]
[Category("Unit")]
public void NetworkProfile_WithValidConfiguration_ShouldValidateSuccessfully()
{
    // Arrange
    var profile = new NetworkProfile
    {
        Name = "Test Profile",
        IPAddress = "192.168.1.100",
        SubnetMask = "255.255.255.0",
        UseDHCP = false
    };

    // Act
    var isValid = profile.IsValid();

    // Assert
    Assert.IsTrue(isValid);
}
```

## 📝 Submitting Changes

### Pull Request Process

1. **Update your branch** with the latest changes from upstream
   ```bash
   git checkout develop
   git pull upstream develop
   git checkout your-feature-branch
   git rebase develop
   ```

2. **Ensure tests pass** and code builds successfully
   ```bash
   dotnet build --configuration Release
   dotnet test
   ```

3. **Create a pull request** with:
   - Clear title and description
   - Reference to related issues
   - Screenshots for UI changes
   - Test results if applicable

4. **Pull request template** will be automatically populated

### Pull Request Requirements

- [ ] Code builds successfully
- [ ] All tests pass
- [ ] Code follows style guidelines
- [ ] Documentation updated (if needed)
- [ ] Breaking changes documented
- [ ] Commits follow conventional commit format

## 🎨 Code Style

### C# Style Guidelines

We follow Microsoft's C# coding conventions with some additions:

- **Use meaningful names** for variables, methods, and classes
- **Follow PascalCase** for public members, camelCase for private members
- **Use explicit access modifiers** (public, private, etc.)
- **Prefer composition over inheritance**
- **Use async/await** for asynchronous operations
- **Handle exceptions appropriately**

### XAML Guidelines

- **Use consistent indentation** (4 spaces)
- **Group related properties** together
- **Use meaningful names** for controls with x:Name
- **Leverage data binding** instead of code-behind when possible
- **Use resources** for repeated styles and templates

### EditorConfig

We use `.editorconfig` to maintain consistent formatting:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
indent_size = 4

[*.{cs,csx,vb,vbx}]
indent_size = 4

[*.{xaml}]
indent_size = 4

[*.{json,yml,yaml}]
indent_size = 2
```

## 📚 Documentation

### Documentation Standards

- **Code comments** should explain WHY, not WHAT
- **XML documentation** for public APIs
- **README updates** for new features
- **Wiki updates** for significant changes

### Documentation Types

1. **Code Documentation**
   ```csharp
   /// <summary>
   /// Scans the specified subnet for active devices using multi-threaded ping operations.
   /// </summary>
   /// <param name="subnet">The subnet information to scan</param>
   /// <param name="options">Scanning configuration options</param>
   /// <param name="cancellationToken">Token for cancelling the operation</param>
   /// <returns>A list of discovered devices with their network information</returns>
   public async Task<List<ScannedDevice>> ScanSubnetAsync(
       SubnetInfo subnet, 
       ScanOptions options, 
       CancellationToken cancellationToken = default)
   ```

2. **User Documentation**
   - Installation guides
   - User manuals
   - Troubleshooting guides
   - FAQ sections

3. **Developer Documentation**
   - API reference
   - Architecture decisions
   - Development setup
   - Contributing guidelines

## 🐛 Bug Reports

### Before Submitting

1. **Search existing issues** to avoid duplicates
2. **Test with latest version** to ensure bug still exists
3. **Gather system information** (OS version, .NET version, etc.)
4. **Create minimal reproduction** case if possible

### Bug Report Template

```markdown
**Describe the bug**
A clear and concise description of what the bug is.

**To Reproduce**
Steps to reproduce the behavior:
1. Go to '...'
2. Click on '....'
3. Scroll down to '....'
4. See error

**Expected behavior**
A clear and concise description of what you expected to happen.

**Screenshots**
If applicable, add screenshots to help explain your problem.

**Environment:**
 - OS: [e.g. Windows 11]
 - .NET Version: [e.g. 6.0.35]
 - Application Version: [e.g. 1.0.0]

**Additional context**
Add any other context about the problem here.
```

## ✨ Feature Requests

### Feature Request Process

1. **Check existing requests** in issues and discussions
2. **Discuss in GitHub Discussions** first for major features
3. **Create detailed feature request** with use cases
4. **Consider implementation complexity** and maintenance impact

### Feature Request Template

```markdown
**Is your feature request related to a problem?**
A clear and concise description of what the problem is.

**Describe the solution you'd like**
A clear and concise description of what you want to happen.

**Describe alternatives you've considered**
A clear and concise description of alternative solutions or features.

**Additional context**
Add any other context or screenshots about the feature request.

**Implementation Ideas**
If you have ideas about how this could be implemented, please share them.
```

## 🏷️ Issue Labels

We use the following labels to categorize issues:

### Type Labels
- `bug` - Something isn't working
- `enhancement` - New feature or request
- `documentation` - Improvements or additions to documentation
- `question` - Further information is requested

### Priority Labels
- `priority: critical` - Critical issues that block functionality
- `priority: high` - Important issues that should be addressed soon
- `priority: medium` - Standard priority
- `priority: low` - Nice to have improvements

### Component Labels
- `component: ui` - User interface related
- `component: scanner` - Network scanning functionality
- `component: profiles` - Profile management
- `component: installer` - Installation and deployment
- `component: core` - Core library functionality

### Status Labels
- `status: needs-review` - Awaiting review
- `status: in-progress` - Currently being worked on
- `status: blocked` - Blocked by external dependency
- `status: help-wanted` - Looking for contributors

## 🌐 Internationalization

### Adding New Languages

1. **Create resource file** in `Resources/Languages/`
   - File naming: `Resources.{language-code}.resx`
   - Example: `Resources.de.resx` for German

2. **Translate all strings** from the base `Resources.en.resx`

3. **Test the translation** in the application

4. **Update language selection** in settings

### Translation Guidelines

- **Maintain context** - Understand where text appears in UI
- **Keep similar length** - UI layouts may break with very long translations
- **Use appropriate formality** - Match the tone of the application
- **Test thoroughly** - Ensure translations work in all contexts

## 🤝 Community

### Communication Channels

- **GitHub Issues** - Bug reports and feature requests
- **GitHub Discussions** - General questions and community discussions
- **Pull Requests** - Code reviews and technical discussions

### Getting Help

1. **Check the wiki** for existing documentation
2. **Search closed issues** for similar problems
3. **Ask in Discussions** for general questions
4. **Create an issue** for bugs or specific problems

### Code Reviews

All submissions require review. We use GitHub pull requests for this purpose. Here's what we look for:

- **Functionality** - Does the code work as intended?
- **Code Quality** - Is the code clean, readable, and maintainable?
- **Performance** - Are there any performance implications?
- **Security** - Are there any security concerns?
- **Testing** - Are there adequate tests?
- **Documentation** - Is documentation updated if needed?

### Recognition

Contributors are recognized in several ways:

- **Contributors section** in README
- **Release notes** mention significant contributors
- **GitHub achievements** for various contribution milestones

## 📋 Checklist for Contributors

Before submitting your contribution, please ensure:

- [ ] Code follows project style guidelines
- [ ] All tests pass locally
- [ ] New tests added for new functionality
- [ ] Documentation updated for new features
- [ ] Commit messages follow conventional format
- [ ] Pull request has clear description
- [ ] No breaking changes (or clearly documented)
- [ ] Security implications considered
- [ ] Performance impact assessed

## 🎉 Thank You!

Thank you for contributing to Perun Network Manager! Your efforts help make this project better for everyone. Whether you're fixing bugs, adding features, improving documentation, or helping other users, every contribution is valuable and appreciated.

---

**Questions?** Feel free to reach out by creating an issue or starting a discussion. We're here to help and excited to see what you'll contribute!

*Happy coding! 🚀*
