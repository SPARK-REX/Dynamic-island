# 🤝 Contributing to Dynamic Island

Thank you for your interest in contributing to **Dynamic Island for Windows**! We welcome bug reports, feature requests, documentation improvements, and pull requests.

---

## 🛠️ Development Setup

1. **Fork and clone** the repository:
   ```cmd
   git clone https://github.com/SPARK-REX/Dynamic-island.git
   cd Dynamic-island
   ```

2. **Build, publish, and test locally:**
   ```cmd
   rebuild.bat
   ```
   > [!TIP]
   > `rebuild.bat` automatically stops any running instances, verifies your .NET 10 SDK, compiles in Release mode, copies assets, and launches the app!

---

## 🧪 Testing Guidelines

Before submitting a Pull Request, please verify that the solution builds cleanly with 0 errors and 0 warnings:
```cmd
dotnet build src/DynamicIsland.sln -c Release --no-incremental
```

---

## 🚀 Submitting Changes

1. Create a new branch:
   ```cmd
   git checkout -b feature/my-new-feature
   ```
2. Commit your changes with clear messages:
   ```cmd
   git commit -m "Add feature XYZ"
   ```
3. Push to your fork and submit a **Pull Request** on GitHub!

---

## ⏱️ Response Time & Issue SLA

We actively review and respond to all issues, pull requests, and discussions within **24 hours**.
