# Windows 應用程式列表取得研究

## 目標

建立一個 .NET Framework 4.8 應用程式，能夠：
1. 取得所有已安裝的 Windows 應用程式（傳統 + Store）
2. 取得每個應用程式的 ICON
3. 盡可能复刻 Windows 的 All Apps List

---

## 一、Windows 應用程式類型

### 1. 傳統桌面應用程式 (.exe)

| 取得方式 | 說明 |
|----------|------|
| Registry | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` |
| Registry (32-bit) | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` |
| Start Menu捷徑 | `%AppData%\Microsoft\Windows\Start Menu\Programs` |
| MsiEnumApplications | Windows Installer API |

### 2. Windows Store 應用程式 (UWP/MSIX)

| 取得方式 | 說明 |
|----------|------|
| Get-AppxPackage | PowerShell / WinRT API |
| PackageManager | Windows.Management.Deployment |
| Shell:AppsFolder | Shell 命名空間 |

### 3. URI 觸發的應用

| 取得方式 | 說明 |
|----------|------|
| Protocol Handler | Registry 中註冊的 URI 協定 |
| App Execution Alias | `%LOCALAPPDATA%\Microsoft\WindowsApps` |

---

## 二、取得 ICON 的方法

### 1. 從 EXE/DLL 擷取

```csharp
using System.Drawing;
using System.Runtime.InteropServices;

public static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
    
    public static Icon ExtractFromFile(string filePath, int iconIndex = 0)
    {
        IntPtr hIcon = ExtractIcon(IntPtr.Zero, filePath, iconIndex);
        if (hIcon == IntPtr.Zero) return null;
        
        Icon icon = Icon.FromHandle(hIcon);
        return icon;
    }
    
    public static Bitmap ExtractAsBitmap(string filePath, int iconIndex = 0)
    {
        using (var icon = ExtractFromFile(filePath, iconIndex))
        {
            return icon?.ToBitmap();
        }
    }
}
```

### 2. 從 Windows Store App 擷取

```csharp
using Windows.Management.Deployment;
using Windows.Storage;

public static class StoreAppIcon
{
    public static async Task<BitmapSource> GetIconAsync(string packageName)
    {
        var packageManager = new PackageManager();
        var package = packageManager.FindPackage(packageName);
        
        // 取得圖示檔案路徑
        var icons = package.InstalledLocation.GetFilesAsync().GetResults();
        var iconFile = icons.FirstOrDefault(f => 
            f.Name.EndsWith(".png") && f.Name.Contains("AppList"));
        
        if (iconFile == null)
        {
            iconFile = icons.FirstOrDefault(f => 
                f.Name.EndsWith(".png") && f.Name.Contains("Square"));
        }
        
        if (iconFile == null) return null;
        
        // 轉換為 Bitmap
        using (var stream = iconFile.OpenReadAsync().GetResults())
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream.AsStream();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            
            return bitmap;
        }
    }
}
```

### 3. 從捷徑取得 ICON (.lnk)

```csharp
using IWshRuntimeLibrary;

public static class ShortcutIcon
{
    public static Icon GetIconFromShortcut(string shortcutPath)
    {
        var shell = new WshShell();
        var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
        
        // 取得目標路徑
        string targetPath = shortcut.TargetPath;
        
        if (!string.IsNullOrEmpty(targetPath) && System.IO.File.Exists(targetPath))
        {
            return IconExtractor.ExtractFromFile(targetPath);
        }
        
        return null;
    }
}
```

---

## 三、取得應用程式列表

### 方法一：Registry（傳統應用）

```csharp
public class InstalledApps
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string RegistryKeyWOW = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    
    public List<AppInfo> GetInstalledApps()
    {
        var apps = new List<AppInfo>();
        
        // 64-bit
        apps.AddRange(ReadRegistryKey(RegistryKey));
        
        // 32-bit
        apps.AddRange(ReadRegistryKey(RegistryKeyWOW));
        
        return apps;
    }
    
    private List<AppInfo> ReadRegistryKey(string keyPath)
    {
        var apps = new List<AppInfo>();
        
        using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
        {
            if (key == null) return apps;
            
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using (var subKey = key.OpenSubKey(subKeyName))
                {
                    var displayName = subKey?.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(displayName)) continue;
                    
                    var app = new AppInfo
                    {
                        Name = displayName,
                        Publisher = subKey?.GetValue("Publisher") as string,
                        InstallLocation = subKey?.GetValue("InstallLocation") as string,
                        UninstallString = subKey?.GetValue("UninstallString") as string,
                        Version = subKey?.GetValue("DisplayVersion") as string,
                        IconPath = subKey?.GetValue("DisplayIcon") as string
                    };
                    
                    apps.Add(app);
                }
            }
        }
        
        return apps;
    }
}
```

### 方法二：WinRT API（Store 應用）

```csharp
using System.Management.Automation;

public class StoreApps
{
    public List<AppInfo> GetStoreApps()
    {
        var apps = new List<AppInfo>();
        
        using (var ps = PowerShell.Create())
        {
            ps.AddScript("Get-AppxPackage | Select-Object Name, DisplayName, Publisher, InstallLocation, PackageFullName");
            
            var results = ps.Invoke();
            
            foreach (var result in results)
            {
                apps.Add(new AppInfo
                {
                    Name = result.Properties["DisplayName"]?.Value?.ToString(),
                    Publisher = result.Properties["Publisher"]?.Value?.ToString(),
                    InstallLocation = result.Properties["InstallLocation"]?.Value?.ToString(),
                    PackageName = result.Properties["PackageFullName"]?.Value?.ToString(),
                    IsStoreApp = true
                });
            }
        }
        
        return apps;
    }
}
```

### 方法三：Shell:AppsFolder

```csharp
using Shell32;

public class ShellAppsFolder
{
    public List<AppInfo> GetAppsFolderApps()
    {
        var apps = new List<AppInfo>();
        
        // 使用 Shell 命名空間
        Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
        dynamic shell = Activator.CreateInstance(shellAppType);
        
        // 開啟 Apps Folder
        var folder = shell.NameSpace("shell:AppsFolder");
        
        foreach (FolderItem item in folder.Items())
        {
            apps.Add(new AppInfo
            {
                Name = item.Name,
                ShellItem = item
            });
        }
        
        return apps;
    }
}
```

---

## 四、整合所有來源

```csharp
public class AppLister
{
    public List<AppInfo> GetAllApps()
    {
        var apps = new List<AppInfo>();
        
        // 1. 傳統應用 (Registry)
        var installedApps = new InstalledApps();
        apps.AddRange(installedApps.GetInstalledApps());
        
        // 2. Store 應用 (WinRT/PowerShell)
        var storeApps = new StoreApps();
        apps.AddRange(storeApps.GetStoreApps());
        
        // 3. Shell AppsFolder
        var shellApps = new ShellAppsFolder();
        apps.AddRange(shellApps.GetAppsFolderApps());
        
        // 去除重複並排序
        return apps
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .GroupBy(a => a.Name)
            .Select(g => g.First())
            .OrderBy(a => a.Name)
            .ToList();
    }
    
    public Bitmap GetAppIcon(AppInfo app)
    {
        if (app.IsStoreApp && !string.IsNullOrEmpty(app.PackageName))
        {
            return StoreAppIcon.GetIconAsync(app.PackageName).Result;
        }
        
        if (!string.IsNullOrEmpty(app.IconPath))
        {
            // 嘗試從路徑擷取
            string iconPath = app.IconPath;
            
            // 處理包含 , 的路徑 (icon index)
            var parts = iconPath.Split(',');
            if (parts.Length > 1 && int.TryParse(parts[1], out int iconIndex))
            {
                return IconExtractor.ExtractAsBitmap(parts[0], iconIndex);
            }
            
            return IconExtractor.ExtractAsBitmap(iconPath);
        }
        
        if (!string.IsNullOrEmpty(app.InstallLocation))
        {
            var exeFiles = Directory.GetFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length > 0)
            {
                return IconExtractor.ExtractAsBitmap(exeFiles[0]);
            }
        }
        
        return null;
    }
}
```

---

## 五、UIA 自動化驗證

### 開啟 All Apps List

```csharp
using System.Windows.Automation;

public class UIALister
{
    public void OpenAllAppsList()
    {
        // 按下 Windows 鍵
        SendKeys.SendWait("^{ESC}");  // 開啟開始功能表
        
        Thread.Sleep(500);
        
        // 按下 All Apps 按鈕
        // 或者使用 UIA 找到並點擊
    }
    
    public List<AppInfo> GetAppsFromStartMenu()
    {
        var apps = new List<AppInfo>();
        
        // 找到開始功能表的 Application 視窗
        var startMenu = AutomationElement.RootElement
            .FindFirst(TreeScope.Children, 
                new PropertyCondition(AutomationElement.ClassNameProperty, "Start"));
        
        if (startMenu == null)
        {
            // 嘗試找到 Application Frame
            startMenu = AutomationElement.RootElement
                .FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "ApplicationFrameWindow"));
        }
        
        // 找到清單項目
        var listItems = startMenu?.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        
        if (listItems != null)
        {
            foreach (AutomationElement item in listItems)
            {
                apps.Add(new AppInfo
                {
                    Name = item.Current.Name,
                    AutomationId = item.Current.AutomationId
                });
            }
        }
        
        return apps;
    }
}
```

### 與 Registry 列表比較

```csharp
public void CompareLists()
{
    var uiApps = new UIALister().GetAppsFromStartMenu();
    var systemApps = new AppLister().GetAllApps();
    
    var uiNames = uiApps.Select(a => a.Name).ToHashSet();
    var systemNames = systemApps.Select(a => a.Name).ToHashSet();
    
    // 僅在 UI 中出現
    var onlyInUI = uiNames.Except(systemNames);
    
    // 僅在系統中出現
    var onlyInSystem = systemNames.Except(uiNames);
    
    // 共同
    var common = uiNames.Intersect(systemNames);
}
```

---

## 六、資料結構

```csharp
public class AppInfo
{
    public string Name { get; set; }
    public string Publisher { get; set; }
    public string Version { get; set; }
    public string InstallLocation { get; set; }
    public string UninstallString { get; set; }
    public string IconPath { get; set; }
    public string PackageName { get; set; }
    public bool IsStoreApp { get; set; }
    public bool IsSystemApp { get; set; }
    public string AutomationId { get; set; }
    
    public Bitmap Icon { get; set; }
    
    public AppSource Source { get; set; }
}

public enum AppSource
{
    Registry,
    StoreApp,
    ShellAppsFolder,
    StartMenu
}
```

---

## 七、執行環境需求

### .NET Framework 4.8

- Windows 10/11
- 需要參考：
  - System.Management.Automation (for PowerShell)
  - Windows.Management.Deployment (via WinRT interop)

### WinRT 相容性

雖然可以使用 WinRT API，但需要：
- 新增 `%ProgramFiles(x86)%\Windows Kits\10\UnionMetadata\Windows.winmd` 參考
- 或使用 PackageManager 類別

---

## 八、驗證方法

### 1. 比對方式

| 比較對象 | 說明 |
|----------|------|
| UIA vs Registry | UI 中的項目是否都有 Registry 紀錄 |
| Store App vs Get-AppxPackage | PowerShell 結果是否完整 |
| ICON 完整性 | 每個應用是否都能取得 ICON |

### 2. 驗證腳本

```csharp
public class Validator
{
    public void Validate()
    {
        var lister = new AppLister();
        var allApps = lister.GetAllApps();
        
        Console.WriteLine($"總應用數: {allApps.Count}");
        
        var withIcons = allApps.Count(a => lister.GetAppIcon(a) != null);
        Console.WriteLine($"有 ICON 的應用: {withIcons}");
        
        var storeApps = allApps.Count(a => a.IsStoreApp);
        Console.WriteLine($"Store 應用: {storeApps}");
        
        var regularApps = allApps.Count(a => !a.IsStoreApp);
        Console.WriteLine($"傳統應用: {regularApps}");
    }
}
```

---

## 九、已知限制

| 限制 | 說明 |
|------|------|
| 權限需求 | 某些 Registry 位置需要管理員權限 |
| Store App ICON | 需要正確的資源路徑 |
| 隱藏應用 | 某些系統應用可能無法取得 |
| UIA 視窗變化 | Windows 版本更新可能影響 UI 結構 |

---

## 十、相關資源

| 資源 | 連結 |
|------|------|
| Get-AppxPackage | https://docs.microsoft.com/en-us/powershell/module/appx/get-appxpackage |
| PackageManager | https://docs.microsoft.com/en-us/windows/win32/appxpkg/manage-appx-with-package-manager |
| UIA Automation | https://docs.microsoft.com/en-us/dotnet/framework/ui-automation/ |

---

## 十一、Windows All Apps List 資料來源分析

### All Apps List 組成

Windows 的 All Apps List 實際上是多個來源的**組合**，包括：

| 來源 | 路徑/位置 | 類型 |
|------|-----------|------|
| **Start Menu 捷徑** | `%AppData%\Microsoft\Windows\Start Menu\Programs` | 使用者捷徑 |
| **All Users Start Menu** | `%ProgramData%\Microsoft\Windows\Start Menu\Programs` | 系統捷徑 |
| **WindowsApps** | `%ProgramFiles%\WindowsApps` | Store App |
| **Packages** | `%LocalAppData%\Packages` | UWP App 快取 |
| **Mr.tCache** | `HKCU\SOFTWARE\Classes\Local Settings\MrtCache` | 資源快取 |
| **Registry** | 多個位置 | Uninstall 資訊 |

### Shell:AppsFolder 組合方式

根據研究，`shell:AppsFolder` 是以下來源的**聯合**：

```
AppsFolder = 
    Start Menu 捷徑 (User)
        + Start Menu 捷徑 (All Users)
        + WindowsApps (Store Apps)
        + MrtCache (資源快取)
        + Registry Uninstall 清單
```

### 關鍵 Registry 位置

| Registry Key | 說明 |
|--------------|------|
| `HKCU\SOFTWARE\Classes\Local Settings\MrtCache` | MRT 快取（包含 Store App 資訊） |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` | 已安裝程式 |
| `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts` | 檔案關聯 |

### 驗證策略

要與 All Apps List 完全一致，必須整合以下來源：

```csharp
public List<AppInfo> GetAllAppsComplete()
{
    var apps = new List<AppInfo>();
    
    // 1. Start Menu 捷徑 (使用者)
    apps.AddRange(GetStartMenuApps(Environment.GetFolderPath(
        Environment.SpecialFolder.StartMenu)));
    
    // 2. Start Menu 捷徑 (All Users)
    apps.AddRange(GetStartMenuApps(Environment.GetFolderPath(
        Environment.SpecialFolder.CommonStartMenu)));
    
    // 3. Shell:AppsFolder
    apps.AddRange(GetAppsFolderApps());
    
    // 4. Registry Uninstall
    apps.AddRange(GetRegistryApps());
    
    // 5. WindowsApps (Store Apps)
    apps.AddRange(GetStoreApps());
    
    // 去除重複並排序
    return apps
        .Where(a => !string.IsNullOrEmpty(a.Name))
        .GroupBy(a => a.Name.ToLowerInvariant())
        .Select(g => g.First())
        .OrderBy(a => a.Name)
        .ToList();
}
```

### UIA 交叉驗證

#### 開啟 All Apps List 並取得項目

```csharp
using System.Windows.Automation;
using System.Runtime.InteropServices;

public class AllAppsListValidator
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    
    private const byte VK_LWIN = 0x5B;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    
    // 開啟 All Apps List
    public void OpenAllAppsList()
    {
        // 按下 Windows 鍵
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        
        Thread.Sleep(300);
        
        // 點擊 "All apps" 按鈕（如果有）
        // 或者使用右鍵 > All Apps
    }
    
    // 使用 UIA 取得 All Apps List 中的所有項目
    public List<string> GetAllAppsFromUI()
    {
        var apps = new List<string>();
        
        // 找到開始功能表視窗
        var startMenu = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, "ApplicationFrameWindow"));
        
        if (startMenu == null) return apps;
        
        // 找到 ScrollViewer 或 List 區域
        var scrollViewer = startMenu.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ClassNameProperty, "ScrollViewer"));
        
        if (scrollViewer != null)
        {
            // 取得所有 ListItem
            var items = scrollViewer.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            
            foreach (AutomationElement item in items)
            {
                apps.Add(item.Current.Name);
            }
        }
        
        return apps;
    }
    
    // 使用更廣泛的搜尋
    public List<AppInfo> GetAllAppsFromStartMenu()
    {
        var apps = new List<AppInfo>();
        
        // 搜尋整個 UI 樹
        var allElements = AutomationElement.RootElement.FindAll(
            TreeScope.Descendants,
            Condition.TrueCondition);
        
        foreach (AutomationElement element in allElements)
        {
            // 檢查是否為應用程式項目
            if (element.Current.ControlType == ControlType.ListItem ||
                element.Current.ControlType == ControlType.Button)
            {
                var name = element.Current.Name;
                if (!string.IsNullOrEmpty(name) && name.Length > 0)
                {
                    // 過濾非應用程式項目
                    if (!IsSystemItem(name))
                    {
                        apps.Add(new AppInfo
                        {
                            Name = name,
                            AutomationId = element.Current.AutomationId,
                            Source = AppSource.StartMenu
                        });
                    }
                }
            }
        }
        
        return apps.Distinct().ToList();
    }
    
    private bool IsSystemItem(string name)
    {
        // 過濾系統項目
        string[] systemItems = new[]
        {
            "Settings", "Power", "File Explorer", "Search",
            "All apps", "Pinned", "Recommended"
        };
        
        return systemItems.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
```

### 交叉比對

```csharp
public class ComparisonResult
{
    public List<string> OnlyInOurList { get; set; }
    public List<string> OnlyInAllAppsList { get; set; }
    public List<string> InBoth { get; set; }
    
    public void Print()
    {
        Console.WriteLine($"=== 比較結果 ===");
        Console.WriteLine($"我們的列表: {OnlyInOurList.Count + InBoth.Count}");
        Console.WriteLine($"All Apps List: {OnlyInAllAppsList.Count + InBoth.Count}");
        Console.WriteLine($"共同: {InBoth.Count}");
        Console.WriteLine($"僅在我們: {OnlyInOurList.Count}");
        Console.WriteLine($"僅在 All Apps: {OnlyInAllAppsList.Count}");
        
        if (OnlyInAllAppsList.Count > 0)
        {
            Console.WriteLine("\n只在 All Apps List:");
            foreach (var app in OnlyInAllAppsList.Take(20))
            {
                Console.WriteLine($"  - {app}");
            }
        }
    }
}

public ComparisonResult CompareWithAllAppsList()
{
    var ourApps = new AppLister().GetAllAppsComplete()
        .Select(a => a.Name.ToLowerInvariant().Trim())
        .ToHashSet();
    
    var uiApps = new AllAppsListValidator().GetAllAppsFromStartMenu()
        .Select(a => a.Name.ToLowerInvariant().Trim())
        .ToHashSet();
    
    var onlyInOurList = ourApps.Except(uiApps).ToList();
    var onlyInAllAppsList = uiApps.Except(ourApps).ToList();
    var inBoth = ourApps.Intersect(uiApps).ToList();
    
    return new ComparisonResult
    {
        OnlyInOurList = onlyInOurList,
        OnlyInAllAppsList = onlyInAllAppsList,
        InBoth = inBoth
    };
}
```

### 完成清單策略

根據研究，要達到與 All Apps List 一致，必須包含：

```csharp
// 完整來源清單
public enum AppDataSource
{
    // Start Menu
    StartMenuUser,           // %AppData%\...\Start Menu\Programs
    StartMenuCommon,         // %ProgramData%\...\Start Menu\Programs
    
    // Shell
    AppsFolder,             // shell:AppsFolder
    
    // Registry
    RegistryUninstall,      // HKLM\...\Uninstall
    RegistryUninstall32,    // HKLM\...\WOW6432Node\...\Uninstall
    MrtCache,               // HKCU\SOFTWARE\Classes\Local Settings\MrtCache
    
    // Store
    AppxPackage,            // Get-AppxPackage
    WindowsApps,            // %ProgramFiles%\WindowsApps
    
    // Others
    Desktop,                // 桌面捷徑
    StartupFolder,          // 啟動資料夾
    SendTo,                 // 傳送到
}
```

---

## 十二、驗證腳本完整範例

```csharp
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Windows 應用程式列表驗證 ===\n");
        
        var validator = new AllAppsListValidator();
        var lister = new AppLister();
        
        // 1. 開啟 All Apps List 並取得 UI 項目
        Console.WriteLine("1. 開啟 All Apps List...");
        validator.OpenAllAppsList();
        Thread.Sleep(1000);
        
        var uiApps = validator.GetAllAppsFromStartMenu();
        Console.WriteLine($"   UI 項目數: {uiApps.Count}");
        
        // 2. 取得我們的完整清單
        Console.WriteLine("2. 取得系統應用程式...");
        var systemApps = lister.GetAllAppsComplete();
        Console.WriteLine($"   系統項目數: {systemApps.Count}");
        
        // 3. 比對
        Console.WriteLine("3. 比對結果...");
        var result = CompareApps(uiApps, systemApps);
        result.Print();
        
        Console.WriteLine("\n=== 驗證完成 ===");
    }
    
    static ComparisonResult CompareApps(List<AppInfo> uiApps, List<AppInfo> systemApps)
    {
        var uiNames = uiApps.Select(a => NormalizeName(a.Name)).ToHashSet();
        var sysNames = systemApps.Select(a => NormalizeName(a.Name)).ToHashSet();
        
        return new ComparisonResult
        {
            OnlyInOurList = sysNames.Except(uiNames).ToList(),
            OnlyInAllAppsList = uiNames.Except(sysNames).ToList(),
            InBoth = sysNames.Intersect(uiNames).ToList()
        };
    }
    
    static string NormalizeName(string name)
    {
        return name?.ToLowerInvariant().Trim() ?? "";
    }
}
```

---

*建立時間: 2026-03-25*
*更新時間: 2026-03-25*

