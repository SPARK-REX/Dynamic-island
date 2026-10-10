# 🤝 Contributing to Dynamic Island

Thank you for your interest in contributing to **Dynamic Island for Windows**! We welcome bug reports, feature requests, code improvements, and documentation fixes.

---

## 🛠️ Development Setup

1. **Fork and clone** the repository:
   ```cmd
   git clone https://github.com/SPARK-REX/Dynamic-Island-for-windows.git
   cd Dynamic-Island-for-windows
   ```

2. **Install dependencies and verify the environment:**
   ```cmd
   setup.bat
   ```

3. **Run the automated test suite:**
   ```cmd
   test.bat
   ```

---

## 🧪 Testing Guidelines

Before submitting a Pull Request, please run the test harness to make sure all 135 unit tests pass:
```cmd
dotnet run -c Release -- --test
```

If you add new features (e.g. new timer calculations, audio helpers, or notification handlers), please add corresponding unit assertions to `IslandLogic.RunAllTests()` in `Program.cs`.

---

## 🚀 Submitting Changes

1. Create a new branch:
   ```cmd
   git checkout -b feature/my-new-feature
   ```
2. Commit your changes:
   ```cmd
   git commit -m "Add feature XYZ"
   ```
3. Push to your fork and submit a **Pull Request** on GitHub!
