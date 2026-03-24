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

*建立時間: 2026-03-25*
