using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed class PortInfo
{
    public string Port;
    public string Description;
}

internal sealed class ComPortOverlay : Form
{
    private readonly Color backgroundColor = Color.FromArgb(22, 27, 38);
    private readonly Color accentColor = Color.FromArgb(86, 194, 180);
    private readonly LogoMark logoMark;
    private readonly Label portSummary;
    private readonly Button settingsButton;
    private readonly ToolTip portTip;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem showTrayItem;
    private readonly ToolStripMenuItem minimizeTrayItem;
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ComPortOverlay", "hidden-ports.txt");
    private readonly HashSet<string> hiddenPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private List<PortInfo> activePorts = new List<PortInfo>();
    private int refreshPending;
    private string lastRender;
    private bool userMinimizedToTray;
    private bool autoHiddenToTray;
    private bool keepVisibleWhileEmpty;

    public ComPortOverlay()
    {
        Text = "COM-порты";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = backgroundColor;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Width = 470;
        Height = 38;
        LoadSettings();

        logoMark = new LogoMark();
        logoMark.Location = new Point(8, 7);
        logoMark.Size = new Size(24, 24);
        Controls.Add(logoMark);

        portSummary = new Label();
        portSummary.Text = "Поиск COM-портов…";
        portSummary.Location = new Point(38, 5);
        portSummary.Size = new Size(Width - 128, 28);
        portSummary.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        portSummary.ForeColor = Color.FromArgb(230, 235, 242);
        portSummary.TextAlign = ContentAlignment.MiddleLeft;
        portSummary.AutoEllipsis = true;
        Controls.Add(portSummary);

        Button minimizeButton = MakeButton("−", Width - 100, delegate { MinimizeToTray(true); });
        minimizeButton.AccessibleName = "Свернуть в трей";
        Controls.Add(minimizeButton);

        settingsButton = MakeButton("⚙", Width - 68, delegate { ShowSettings(); });
        settingsButton.AccessibleName = "Настройки COM-портов";
        Controls.Add(settingsButton);

        Button closeButton = MakeButton("×", Width - 36, delegate { Close(); });
        closeButton.AccessibleName = "Закрыть";
        Controls.Add(closeButton);

        portTip = new ToolTip();
        portTip.AutoPopDelay = 15000;
        portTip.InitialDelay = 450;
        portTip.ReshowDelay = 150;
        portTip.SetToolTip(logoMark, "COM-порты");
        portTip.SetToolTip(settingsButton, "Настройки COM-портов");
        portTip.SetToolTip(minimizeButton, "Свернуть в трей");
        portTip.SetToolTip(closeButton, "Закрыть");
        portSummary.ContextMenuStrip = BuildContextMenu();
        ContextMenuStrip = portSummary.ContextMenuStrip;

        trayMenu = new ContextMenuStrip();
        showTrayItem = new ToolStripMenuItem("Показать панель");
        showTrayItem.Click += delegate { ShowPanelFromTray(); };
        minimizeTrayItem = new ToolStripMenuItem("Свернуть в трей");
        minimizeTrayItem.Click += delegate { MinimizeToTray(true); };
        trayMenu.Items.Add(showTrayItem);
        trayMenu.Items.Add("Настройки COM-портов…", null, delegate { ShowSettings(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(minimizeTrayItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Выход", null, delegate { Close(); });
        trayMenu.Opening += delegate
        {
            showTrayItem.Enabled = !Visible;
            minimizeTrayItem.Enabled = Visible;
        };

        trayIcon = new NotifyIcon();
        trayIcon.Icon = CreateTrayIcon();
        trayIcon.Text = "COM-порты: выполняется поиск";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        Icon = trayIcon.Icon;
        trayIcon.DoubleClick += delegate
        {
            if (Visible) MinimizeToTray(true);
            else ShowPanelFromTray();
        };

        refreshTimer = new System.Windows.Forms.Timer();
        refreshTimer.Interval = 1800;
        refreshTimer.Tick += delegate { RequestRefresh(); };
        refreshTimer.Start();

        FormClosed += delegate
        {
            refreshTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            portTip.Dispose();
        };

        Shown += delegate
        {
            PlaceAboveTaskbarClock();
            RequestRefresh();
        };
        Resize += delegate { UpdateRoundedCorners(); };
        UpdateRoundedCorners();
    }

    private Button MakeButton(string text, int left, EventHandler click)
    {
        Button button = new Button();
        button.Text = text;
        button.Font = new Font("Segoe UI Symbol", 10F, FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = Color.FromArgb(177, 190, 207);
        button.BackColor = backgroundColor;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 51, 67);
        button.Size = new Size(28, 28);
        button.Location = new Point(left, 5);
        button.Cursor = Cursors.Hand;
        button.Click += click;
        return button;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Настройки COM-портов…", null, delegate { ShowSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Свернуть в трей", null, delegate { MinimizeToTray(true); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Обновить", null, delegate { RequestRefresh(); });
        menu.Items.Add("Выход", null, delegate { Close(); });
        return menu;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    private void PlaceAboveTaskbarClock()
    {
        Screen screen = Screen.PrimaryScreen;
        if (screen == null) return;
        Rectangle area = screen.WorkingArea;
        Left = area.Right - Width - 6;
        Top = area.Bottom - Height - 4;
    }

    private void UpdateRoundedCorners()
    {
        if (Width < 2 || Height < 2) return;
        using (GraphicsPath path = new GraphicsPath())
        {
            int radius = 10;
            int diameter = radius * 2;
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
            path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
            path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }
    }

    private void MinimizeToTray(bool manual)
    {
        if (manual)
        {
            userMinimizedToTray = true;
            autoHiddenToTray = false;
            keepVisibleWhileEmpty = false;
        }
        else
        {
            autoHiddenToTray = true;
        }
        trayIcon.Visible = true;
        Hide();
    }

    private void ShowPanelFromTray()
    {
        userMinimizedToTray = false;
        autoHiddenToTray = false;
        keepVisibleWhileEmpty = !activePorts.Any(p => !hiddenPorts.Contains(p.Port));
        PlaceAboveTaskbarClock();
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;
        BringToFront();
    }

    private void UpdateTrayAndVisibility(int visiblePortCount)
    {
        trayIcon.Visible = true;
        string portsText = String.Join(", ", activePorts.Select(p => p.Port).ToArray());
        string tooltip = activePorts.Count == 0 ? "COM-порты: нет подключений" : "COM-порты: " + portsText;
        if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 60) + "…";
        trayIcon.Text = tooltip;

        if (visiblePortCount == 0)
        {
            if (Visible && !userMinimizedToTray && !keepVisibleWhileEmpty)
                MinimizeToTray(false);
        }
        else
        {
            keepVisibleWhileEmpty = false;
            if (autoHiddenToTray && !userMinimizedToTray)
            {
                autoHiddenToTray = false;
                ShowPanelFromTray();
            }
        }
    }

    private static Icon CreateTrayIcon()
    {
        using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                DrawBrandLogo(graphics, new Rectangle(0, 0, 32, 32));
            }
            IntPtr iconHandle = bitmap.GetHicon();
            try
            {
                Icon borrowedIcon = Icon.FromHandle(iconHandle);
                return (Icon)borrowedIcon.Clone();
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }
    }

    internal static void DrawBrandLogo(Graphics graphics, Rectangle bounds)
    {
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(bounds.Left, bounds.Top);
        graphics.ScaleTransform(bounds.Width / 32F, bounds.Height / 32F);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (GraphicsPath tile = RoundedRectangle(1, 1, 30, 30, 8))
        using (SolidBrush tileBrush = new SolidBrush(Color.FromArgb(24, 34, 48)))
        using (Pen tileOutline = new Pen(Color.FromArgb(64, 109, 116), 1.1F))
        {
            graphics.FillPath(tileBrush, tile);
            graphics.DrawPath(tileOutline, tile);

            using (GraphicsPath shell = new GraphicsPath())
            using (SolidBrush shellBrush = new SolidBrush(Color.FromArgb(38, 83, 94)))
            using (Pen shellPen = new Pen(Color.FromArgb(112, 220, 204), 1.35F))
            {
                shell.AddPolygon(new PointF[]
                {
                    new PointF(5.5F, 7.5F), new PointF(26.5F, 7.5F),
                    new PointF(23.5F, 22.8F), new PointF(21.5F, 25.0F),
                    new PointF(10.5F, 25.0F), new PointF(8.5F, 22.8F)
                });
                graphics.FillPath(shellBrush, shell);
                graphics.DrawPath(shellPen, shell);

                using (GraphicsPath inset = new GraphicsPath())
                using (SolidBrush insetBrush = new SolidBrush(Color.FromArgb(22, 42, 54)))
                {
                    inset.AddPolygon(new PointF[]
                    {
                        new PointF(8.0F, 10.0F), new PointF(24.0F, 10.0F),
                        new PointF(21.8F, 21.8F), new PointF(10.2F, 21.8F)
                    });
                    graphics.FillPath(insetBrush, inset);
                }
            }

            using (SolidBrush pinBrush = new SolidBrush(Color.FromArgb(224, 250, 245)))
            {
                for (int column = 0; column < 5; column++)
                    graphics.FillEllipse(pinBrush, 9.25F + column * 3.2F, 12.2F, 1.5F, 1.5F);
                for (int column = 0; column < 4; column++)
                    graphics.FillEllipse(pinBrush, 10.85F + column * 3.2F, 17.5F, 1.5F, 1.5F);
            }
        }
        graphics.Restore(state);
    }

    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            List<PortInfo> ports = ReadPorts();
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        RenderPorts(ports);
                        Interlocked.Exchange(ref refreshPending, 0);
                    });
                }
                else
                {
                    Interlocked.Exchange(ref refreshPending, 0);
                }
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref refreshPending, 0); }
        });
    }

    private static List<PortInfo> ReadPorts()
    {
        Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        string name = Convert.ToString(item["Name"]);
                        if (String.IsNullOrWhiteSpace(name)) continue;
                        Match match = Regex.Match(name, @"\((COM\d+)\)\s*$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string port = match.Groups[1].Value.ToUpperInvariant();
                            string description = Regex.Replace(name, @"\s*\(COM\d+\)\s*$", "", RegexOptions.IgnoreCase).Trim();
                            descriptions[port] = description;
                        }
                    }
                }
            }
        }
        catch { }

        string[] names;
        try { names = SerialPort.GetPortNames(); }
        catch { names = new string[0]; }

        List<PortInfo> ports = new List<PortInfo>();
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string port = name.ToUpperInvariant();
            string description;
            if (!descriptions.TryGetValue(port, out description)) description = "Последовательный порт";
            ports.Add(new PortInfo { Port = port, Description = description });
        }
        SortPorts(ports);
        return ports;
    }

    private static void SortPorts(List<PortInfo> ports)
    {
        ports.Sort(delegate(PortInfo a, PortInfo b)
        {
            return GetPortNumber(a.Port).CompareTo(GetPortNumber(b.Port));
        });
    }

    private static int GetPortNumber(string port)
    {
        Match match = Regex.Match(port, @"\d+");
        int number;
        return match.Success && Int32.TryParse(match.Value, out number) ? number : Int32.MaxValue;
    }

    private void RenderPorts(List<PortInfo> ports)
    {
        activePorts = ports;
        List<PortInfo> visiblePorts = ports.Where(p => !hiddenPorts.Contains(p.Port)).ToList();
        UpdateTrayAndVisibility(visiblePorts.Count);
        string fingerprint = String.Join("\n", ports.Select(p => p.Port + "|" + p.Description).ToArray())
            + "\nH:" + String.Join(",", hiddenPorts.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray());
        if (fingerprint == lastRender) return;
        lastRender = fingerprint;

        string displayText;
        string fullText;
        if (visiblePorts.Count > 0)
        {
            displayText = String.Join("   ", visiblePorts.Select(p => p.Port).ToArray());
            fullText = String.Join(Environment.NewLine, visiblePorts.Select(p => p.Port).ToArray());
        }
        else if (ports.Count > 0)
        {
            displayText = "Все COM-порты скрыты";
            fullText = "Все подключённые порты скрыты. Нажмите ⚙, чтобы изменить настройки.";
        }
        else
        {
            displayText = "Нет подключённых COM-портов";
            fullText = displayText;
        }

        portSummary.Text = displayText;
        portTip.SetToolTip(portSummary, fullText);
        PlaceAboveTaskbarClock();
    }

    private void ShowSettings()
    {
        Dictionary<string, PortInfo> current = activePorts.ToDictionary(p => p.Port, p => p, StringComparer.OrdinalIgnoreCase);
        List<PortInfo> options = new List<PortInfo>(activePorts);
        foreach (string hidden in hiddenPorts)
        {
            if (!current.ContainsKey(hidden))
                options.Add(new PortInfo { Port = hidden, Description = "Сейчас не подключён" });
        }
        SortPorts(options);

        using (Form dialog = new Form())
        {
            dialog.Text = "Настройки COM-портов";
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.ClientSize = new Size(440, 330);
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label instruction = new Label();
            instruction.Text = "Отметьте галочками порты, которые нужно скрыть:";
            instruction.Location = new Point(14, 12);
            instruction.Size = new Size(412, 24);
            dialog.Controls.Add(instruction);

            ListView list = new ListView();
            list.Location = new Point(14, 42);
            list.Size = new Size(412, 202);
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Порт", 82);
            list.Columns.Add("Устройство / состояние", 310);
            foreach (PortInfo option in options)
            {
                ListViewItem item = new ListViewItem(option.Port);
                item.SubItems.Add(option.Description);
                item.Checked = hiddenPorts.Contains(option.Port);
                list.Items.Add(item);
            }
            dialog.Controls.Add(list);

            CheckBox startupCheck = new CheckBox();
            startupCheck.Text = "Запускать при входе в Windows (копировать в папку пользователя)";
            startupCheck.Location = new Point(14, 250);
            startupCheck.Size = new Size(412, 24);
            startupCheck.Checked = IsRunAtStartup();
            dialog.Controls.Add(startupCheck);

            Button saveButton = new Button();
            saveButton.Text = "Сохранить";
            saveButton.Location = new Point(246, 286);
            saveButton.Size = new Size(88, 28);
            saveButton.DialogResult = DialogResult.None;
            dialog.Controls.Add(saveButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Отмена";
            cancelButton.Location = new Point(342, 286);
            cancelButton.Size = new Size(84, 28);
            cancelButton.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(cancelButton);
            dialog.CancelButton = cancelButton;

            saveButton.Click += delegate
            {
                HashSet<string> updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ListViewItem item in list.Items)
                {
                    if (item.Checked) updated.Add(item.Text);
                }
                bool previousStartupSetting = IsRunAtStartup();
                if (!SetRunAtStartup(startupCheck.Checked)) return;
                if (!PersistHiddenPorts(updated))
                {
                    if (previousStartupSetting != startupCheck.Checked)
                        SetRunAtStartup(previousStartupSetting);
                    return;
                }

                hiddenPorts.Clear();
                foreach (string port in updated) hiddenPorts.Add(port);
                lastRender = null;
                RenderPorts(activePorts);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            dialog.AcceptButton = saveButton;
            dialog.ShowDialog(this);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            foreach (string line in File.ReadAllLines(settingsPath))
            {
                string port = line.Trim().ToUpperInvariant();
                if (Regex.IsMatch(port, @"^COM\d+$")) hiddenPorts.Add(port);
            }
        }
        catch { }
    }

    private bool PersistHiddenPorts(HashSet<string> ports)
    {
        try
        {
            string directory = Path.GetDirectoryName(settingsPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(settingsPath, ports.OrderBy(p => GetPortNumber(p)).ToArray());
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить настройки:\r\n" + ex.Message,
                "Настройки COM-портов", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool IsRunAtStartup()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key == null) return false;
                return key.GetValue("ComPortOverlayTray") != null;
            }
        }
        catch { return false; }
    }

    private bool SetRunAtStartup(bool enabled)
    {
        try
        {
            string startupExecutable = Application.ExecutablePath;
            if (enabled)
                startupExecutable = CopyToUserApplicationFolder();

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (key == null) throw new InvalidOperationException("Не удалось открыть раздел автозапуска.");
                if (enabled)
                    key.SetValue("ComPortOverlayTray", "\"" + startupExecutable + "\"", RegistryValueKind.String);
                else
                    key.DeleteValue("ComPortOverlayTray", false);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось подготовить копию программы или изменить автозапуск:\r\n" + ex.Message,
                "Автозапуск COM-панели", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static string CopyToUserApplicationFolder()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (String.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("Не удалось определить папку Local AppData пользователя.");

        string destination = Path.Combine(localAppData, "ComPortOverlay", "ComPortOverlay.exe");
        string sourcePath = Path.GetFullPath(Application.ExecutablePath);
        string destinationPath = Path.GetFullPath(destination);

        if (!String.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            string directory = Path.GetDirectoryName(destinationPath);
            Directory.CreateDirectory(directory);
            File.Copy(sourcePath, destinationPath, true);
        }

        return destinationPath;
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ComPortOverlay());
    }
}

internal sealed class LogoMark : Control
{
    public LogoMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ComPortOverlay.DrawBrandLogo(e.Graphics, ClientRectangle);
    }
}
