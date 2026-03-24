using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace AppList
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

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
        public string Source { get; set; }
        public Bitmap Icon { get; set; }
    }

    public class MainForm : Form
    {
        private ListView listView;
        private Button btnRefresh;
        private Button btnCompare;
        private Label lblStatus;
        private ProgressBar progressBar;
        
        public MainForm()
        {
            InitializeComponent();
        }
        
        private void InitializeComponent()
        {
            this.Text = "Windows App List - 應用程式列表";
            this.Size = new Size(900, 600);
            
            // Status Label
            lblStatus = new Label
            {
                Text = "點擊 [取得清單] 開始...",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            
            // Progress Bar
            progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 10,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            
            // Buttons
            btnRefresh = new Button
            {
                Text = "取得清單",
                Dock = DockStyle.Top,
                Height = 40,
                Top = 30
            };
            btnRefresh.Click += BtnRefresh_Click;
            
            btnCompare = new Button
            {
                Text = "與 All Apps List 比對",
                Dock = DockStyle.Top,
                Height = 40,
                Top = 70
            };
            btnCompare.Click += BtnCompare_Click;
            
            // ListView
            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                SmallImageList = new ImageList()
            };
            
            listView.Columns.Add("名稱", 300);
            listView.Columns.Add("來源", 150);
            listView.Columns.Add("發行者", 200);
            listView.Columns.Add("版本", 100);
            
            this.Controls.Add(listView);
            this.Controls.Add(btnCompare);
            this.Controls.Add(btnRefresh);
            this.Controls.Add(progressBar);
            this.Controls.Add(lblStatus);
        }
        
        private async void BtnRefresh_Click(object sender, EventArgs e)
        {
            progressBar.Visible = true;
            lblStatus.Text = "正在取得應用程式列表...";
            
            var apps = await Task.Run(() => GetAllApps());
            
            listView.Items.Clear();
            listView.SmallImageList.Images.Clear();
            
            int count = 0;
            foreach (var app in apps.Take(500)) // 限制顯示數量
            {
                var item = new ListViewItem(app.Name);
                item.SubItems.Add(app.Source);
                item.SubItems.Add(app.Publisher ?? "");
                item.SubItems.Add(app.Version ?? "");
                
                listView.Items.Add(item);
                count++;
            }
            
            lblStatus.Text = $"共取得 {count} 個應用程式";
            progressBar.Visible = false;
        }
        
        private void BtnCompare_Click(object sender, EventArgs e)
        {
            MessageBox.Show("請手動開啟 All Apps List (按 Windows 鍵)，然後進行比對。", 
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        
        private List<AppInfo> GetAllApps()
        {
            var apps = new List<AppInfo>();
            
            // 1. Registry Uninstall
            apps.AddRange(GetRegistryApps());
            
            // 2. Start Menu
            apps.AddRange(GetStartMenuApps(Environment.GetFolderPath(
                Environment.SpecialFolder.StartMenu)));
            apps.AddRange(GetStartMenuApps(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonStartMenu)));
            
            // 3. Shell AppsFolder
            apps.AddRange(GetAppsFolderApps());
            
            return apps
                .Where(a => !string.IsNullOrEmpty(a.Name))
                .GroupBy(a => a.Name.ToLowerInvariant().Trim())
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private List<AppInfo> GetRegistryApps()
        {
            var apps = new List<AppInfo>();
            
            string[] keys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            
            foreach (var keyPath in keys)
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) continue;
                    
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(displayName)) continue;
                            
                            apps.Add(new AppInfo
                            {
                                Name = displayName,
                                Publisher = subKey?.GetValue("Publisher") as string,
                                Version = subKey?.GetValue("DisplayVersion") as string,
                                InstallLocation = subKey?.GetValue("InstallLocation") as string,
                                IconPath = subKey?.GetValue("DisplayIcon") as string,
                                Source = "Registry"
                            });
                        }
                    }
                }
            }
            
            return apps;
        }
        
        private List<AppInfo> GetStartMenuApps(string folderPath)
        {
            var apps = new List<AppInfo>();
            
            if (!Directory.Exists(folderPath)) return apps;
            
            foreach (var file in Directory.GetFiles(folderPath, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    apps.Add(new AppInfo
                    {
                        Name = name,
                        IconPath = file,
                        Source = "StartMenu"
                    });
                }
                catch { }
            }
            
            return apps;
        }
        
        private List<AppInfo> GetAppsFolderApps()
        {
            var apps = new List<AppInfo>();
            
            try
            {
                Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(shellAppType);
                var folder = shell.NameSpace("shell:AppsFolder");
                
                if (folder != null)
                {
                    foreach (FolderItem2 item in folder.Items())
                    {
                        apps.Add(new AppInfo
                        {
                            Name = item.Name,
                            Source = "AppsFolder"
                        });
                    }
                }
            }
            catch { }
            
            return apps;
        }
    }
}
