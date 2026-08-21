using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("OwO Screen Veil")]
[assembly: AssemblyDescription("Desktop privacy overlay utility with black-screen-only Windows key recovery.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Leon OwO")]
[assembly: AssemblyProduct("Leon OwO Utilities")]
[assembly: AssemblyCopyright("Copyright © 2026 Leon OwO. All rights reserved.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

public class MainForm : Form
{
    private Label lblStatusValue;
    private Label lblHotkeyValue;

    private RadioButton rdoSingleKey;
    private RadioButton rdoComboKey;

    private CheckBox chkCtrl;
    private CheckBox chkAlt;
    private CheckBox chkShift;

    private ComboBox cmbKey;

    private CheckBox chkStartMinimized;
    private CheckBox chkAutoStart;
    private CheckBox chkKeepAwake;
    private CheckBox chkMinimizeToTray;
    private CheckBox chkPasswordProtection;

    private CenteredPasswordTextBox txtUnlockPassword;
    private Button btnApply;

    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private ToolStripMenuItem menuOpen;
    private ToolStripMenuItem menuExit;

    private Timer statusTimer;
    private Timer keepAwakeRefreshTimer;

    private bool hotkeyRegistered = false;
    private bool hasAutoMinimized = false;
    private bool isLoadingSettings = false;
    private bool useSingleKey = true;
    private bool internalTrayMinimize = false;

    private int currentModifiers = 0;
    private Keys currentKey = Keys.Pause;
    private Keys currentDisplayKey = Keys.Pause;

    private string temporaryStatusMessage = "";
    private Color temporaryStatusColor = SystemColors.ControlText;
    private DateTime temporaryStatusUntil = DateTime.MinValue;

    private LowRiskBlackOverlayForm overlayForm;
    private HotkeyMessageWindow hotkeyWindow;

    private const int HOTKEY_ID = 1001;

    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_NOREPEAT = 0x4000;

    private const string APP_NAME = "OwO Screen Veil";
    private const string SETTINGS_KEY_PATH = @"Software\Leon OwO\OwO Screen Veil";
    private const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RUN_VALUE_NAME = "OwO Screen Veil";
    private const string LEGACY_RUN_VALUE_NAME_1 = "OWLScreenVeil";
    private const string LEGACY_RUN_VALUE_NAME_2 = "OwOScreenVeil";

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private const int KEEP_AWAKE_REFRESH_INTERVAL_MS = 30000;

    public MainForm()
    {
        hotkeyWindow = new HotkeyMessageWindow();
        hotkeyWindow.HotkeyPressed += delegate
        {
            ExecuteHotkeyAction();
        };

        overlayForm = new LowRiskBlackOverlayForm();
        overlayForm.PasswordAccepted += delegate
        {
            HideBlackOverlayAfterPassword();
        };

        InitializeForm();
        InitializeTray();
        InitializeStatusTimer();
        InitializeKeepAwakeRefreshTimer();

        LoadSettings();
        UpdateHotkeyModeState();
        UpdatePasswordInputState();
        ApplyHotkey();
        ApplyKeepAwakeSetting(false);
        UpdateUI();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        /*
         * 視窗第一次顯示完成後，如果已啟用「啟動時隱藏」，
         * 就自動縮小到系統匣，並顯示提示訊息。
         *
         * 不在建構子內直接呼叫 Hide()，
         * 避免 Form 尚未完成顯示流程時直接隱藏，
         * 造成程式看起來像沒有啟動。
         */
        if (!hasAutoMinimized &&
            chkStartMinimized != null &&
            chkStartMinimized.Checked)
        {
            hasAutoMinimized = true;

            HideToTray();

            if (trayIcon != null)
            {
                trayIcon.ShowBalloonTip(
                    2000,
                    APP_NAME,
                    "已縮小到系統匣，雙擊圖示可開啟。",
                    ToolTipIcon.Info);
            }
        }
    }

    private void InitializeForm()
    {
        Text = APP_NAME;
        Icon = GetApplicationIcon();
        ClientSize = new Size(420, 390);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        int leftLabelX = 20;
        int valueX = 120;
        int valueWidth = 270;
        int y = 20;

        CreateCaption("狀態：", leftLabelX, y);
        lblStatusValue = CreateValueLabel(valueX, y, valueWidth);

        y += 35;
        CreateCaption("觸發方式：", leftLabelX, y);

        rdoSingleKey = new RadioButton();
        rdoSingleKey.Text = "單一按鍵";
        rdoSingleKey.SetBounds(valueX, y - 2, 90, 24);
        rdoSingleKey.CheckedChanged += delegate { UpdateHotkeyModeState(); };

        rdoComboKey = new RadioButton();
        rdoComboKey.Text = "組合按鍵";
        rdoComboKey.SetBounds(valueX + 100, y - 2, 90, 24);
        rdoComboKey.CheckedChanged += delegate { UpdateHotkeyModeState(); };

        y += 35;
        CreateCaption("按鍵設定：", leftLabelX, y);

        chkCtrl = new CheckBox();
        chkCtrl.Text = "Ctrl";
        chkCtrl.SetBounds(valueX, y - 2, 55, 24);

        chkAlt = new CheckBox();
        chkAlt.Text = "Alt";
        chkAlt.SetBounds(valueX + 60, y - 2, 50, 24);

        chkShift = new CheckBox();
        chkShift.Text = "Shift";
        chkShift.SetBounds(valueX + 115, y - 2, 60, 24);

        y += 35;
        cmbKey = new ComboBox();
        cmbKey.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbKey.SetBounds(valueX, y - 2, 100, 24);
        AddKeyItems();

        btnApply = new Button();
        btnApply.Text = "套用設定";
        btnApply.SetBounds(valueX + 115, y - 4, 85, 28);
        btnApply.Click += delegate { ApplyHotkey(); };

        y += 35;
        CreateCaption("目前設定：", leftLabelX, y);
        lblHotkeyValue = CreateValueLabel(valueX, y, valueWidth);

        y += 35;
        CreateCaption("其他設定：", leftLabelX, y);

        chkStartMinimized = CreateOptionCheckBox("啟動時隱藏", valueX, y, 210);
        chkStartMinimized.CheckedChanged += delegate { SaveOptionSetting("啟動時隱藏", chkStartMinimized.Checked); };

        y += 30;
        chkAutoStart = CreateOptionCheckBox("隨 Windows 啟動", valueX, y, 190);
        chkAutoStart.CheckedChanged += delegate
        {
            if (!isLoadingSettings)
            {
                if (ApplyAutoStartSetting())
                {
                    SaveOptionSetting("隨 Windows 啟動", chkAutoStart.Checked);
                }
                else
                {
                    SaveSettings();
                    UpdateUI();
                }
            }
        };

        y += 30;
        chkKeepAwake = CreateOptionCheckBox("工作模式（維持活動狀態）", valueX, y, 240);
        chkKeepAwake.CheckedChanged += delegate
        {
            if (!isLoadingSettings)
            {
                ApplyKeepAwakeSetting(true);
                SaveSettings();
                UpdateUI();
            }
        };

        y += 30;
        chkMinimizeToTray = CreateOptionCheckBox("最小化至系統匣", valueX, y, 210);
        chkMinimizeToTray.CheckedChanged += delegate { SaveOptionSetting("最小化至系統匣", chkMinimizeToTray.Checked); };

        y += 30;
        chkPasswordProtection = CreateOptionCheckBox("啟用密碼保護", valueX, y, 210);
        chkPasswordProtection.CheckedChanged += delegate
        {
            UpdatePasswordInputState();
            SaveOptionSetting("密碼保護", chkPasswordProtection.Checked);
        };

        y += 30;
        CreateCaption("解除密碼：", leftLabelX, y);

        txtUnlockPassword = new CenteredPasswordTextBox();
        txtUnlockPassword.PlaceholderText = "請輸入密碼";
        txtUnlockPassword.PasswordChar = '●';
        txtUnlockPassword.SetBounds(valueX, y - 2, 160, 24);
        txtUnlockPassword.TextChanged += delegate
        {
            if (!isLoadingSettings)
            {
                SaveSettings();
            }
        };

        Controls.Add(rdoSingleKey);
        Controls.Add(rdoComboKey);
        Controls.Add(chkCtrl);
        Controls.Add(chkAlt);
        Controls.Add(chkShift);
        Controls.Add(cmbKey);
        Controls.Add(btnApply);
        Controls.Add(chkStartMinimized);
        Controls.Add(chkAutoStart);
        Controls.Add(chkKeepAwake);
        Controls.Add(chkMinimizeToTray);
        Controls.Add(chkPasswordProtection);
        Controls.Add(txtUnlockPassword);
    }

    private CheckBox CreateOptionCheckBox(string text, int x, int y, int width)
    {
        CheckBox checkBox = new CheckBox();
        checkBox.Text = text;
        checkBox.SetBounds(x, y - 2, width, 24);
        return checkBox;
    }

    private void SaveOptionSetting(string optionName, bool enabled)
    {
        if (!isLoadingSettings)
        {
            SaveSettings();
            if (enabled) ShowStatusEnabled(optionName);
            else ShowStatusCancelled(optionName);
            UpdateUI();
        }
    }

    private void CreateCaption(string text, int x, int y)
    {
        Label label = new Label();
        label.Text = text;
        label.SetBounds(x, y, 90, 22);
        label.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(label);
    }

    private Label CreateValueLabel(int x, int y, int width)
    {
        Label label = new Label();
        label.SetBounds(x, y, width, 22);
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(label);
        return label;
    }

    private void AddKeyItems()
    {
        int i;
        for (i = (int)Keys.A; i <= (int)Keys.Z; i++) cmbKey.Items.Add(((Keys)i).ToString());
        for (i = (int)Keys.D0; i <= (int)Keys.D9; i++) cmbKey.Items.Add(((Keys)i).ToString());
        for (i = (int)Keys.F1; i <= (int)Keys.F12; i++) cmbKey.Items.Add(((Keys)i).ToString());

        cmbKey.Items.Add("Insert");
        cmbKey.Items.Add("Delete");
        cmbKey.Items.Add("Home");
        cmbKey.Items.Add("End");
        cmbKey.Items.Add("PageUp");
        cmbKey.Items.Add("PageDown");
        cmbKey.Items.Add("Pause");
        cmbKey.Items.Add("Scroll");
    }

    private void InitializeStatusTimer()
    {
        statusTimer = new Timer();
        statusTimer.Interval = 500;
        statusTimer.Tick += delegate { UpdateUI(); };
        statusTimer.Start();
    }

    private void InitializeKeepAwakeRefreshTimer()
    {
        keepAwakeRefreshTimer = new Timer();
        keepAwakeRefreshTimer.Interval = KEEP_AWAKE_REFRESH_INTERVAL_MS;
        keepAwakeRefreshTimer.Tick += delegate { RefreshKeepAwakeState(false); };
    }

    private void UpdateHotkeyModeState()
    {
        if (rdoSingleKey == null) return;

        useSingleKey = rdoSingleKey.Checked;
        bool enableModifiers = !useSingleKey;

        if (chkCtrl != null) chkCtrl.Enabled = enableModifiers;
        if (chkAlt != null) chkAlt.Enabled = enableModifiers;
        if (chkShift != null) chkShift.Enabled = enableModifiers;

        if (useSingleKey)
        {
            if (chkCtrl != null) chkCtrl.Checked = false;
            if (chkAlt != null) chkAlt.Checked = false;
            if (chkShift != null) chkShift.Checked = false;
        }
    }

    private void UpdatePasswordInputState()
    {
        bool passwordEnabled = chkPasswordProtection != null && chkPasswordProtection.Checked;
        if (txtUnlockPassword != null)
        {
            txtUnlockPassword.Enabled = passwordEnabled;
            txtUnlockPassword.PlaceholderText = passwordEnabled ? "請輸入密碼" : "";
        }
    }

    private void InitializeTray()
    {
        trayMenu = new ContextMenuStrip();

        menuOpen = new ToolStripMenuItem("開啟面板");
        menuOpen.Click += delegate { ShowFromTray(); };

        menuExit = new ToolStripMenuItem("結束程式");
        menuExit.Click += delegate { CloseProgram(); };

        trayMenu.Items.Add(menuOpen);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(menuExit);

        trayIcon = new NotifyIcon();
        trayIcon.Icon = GetApplicationIcon();
        trayIcon.Text = APP_NAME;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { ShowFromTray(); };
    }

    private void LoadSettings()
    {
        isLoadingSettings = true;

        try
        {
            bool startMinimized = false;
            bool autoStart = false;
            bool keepAwake = false;
            bool minimizeToTray = true;
            bool passwordProtection = false;
            string unlockPassword = "";
            bool loadedSingleKey = true;
            bool loadedCtrl = false;
            bool loadedAlt = false;
            bool loadedShift = false;
            string loadedKey = "Pause";

            using (RegistryKey settingsKey = Registry.CurrentUser.OpenSubKey(SETTINGS_KEY_PATH, false))
            {
                if (settingsKey != null)
                {
                    startMinimized = ReadRegistryBoolean(settingsKey, "StartMinimized", false);
                    autoStart = ReadRegistryBoolean(settingsKey, "AutoStart", false);
                    keepAwake = ReadRegistryBoolean(settingsKey, "KeepAwake", false);
                    minimizeToTray = ReadRegistryBoolean(settingsKey, "MinimizeToTray", true);
                    passwordProtection = ReadRegistryBoolean(settingsKey, "PasswordProtection", false);
                    loadedSingleKey = ReadRegistryBoolean(settingsKey, "SingleKey", true);
                    loadedCtrl = ReadRegistryBoolean(settingsKey, "Ctrl", false);
                    loadedAlt = ReadRegistryBoolean(settingsKey, "Alt", false);
                    loadedShift = ReadRegistryBoolean(settingsKey, "Shift", false);
                    loadedKey = ReadRegistryString(settingsKey, "Key", "Pause");
                    unlockPassword = UnprotectPassword(settingsKey.GetValue("UnlockPassword") as byte[]);
                }
            }

            chkStartMinimized.Checked = startMinimized;
            chkAutoStart.Checked = autoStart;
            chkKeepAwake.Checked = keepAwake;
            chkMinimizeToTray.Checked = minimizeToTray;
            chkPasswordProtection.Checked = passwordProtection;
            txtUnlockPassword.Text = unlockPassword;

            rdoSingleKey.Checked = loadedSingleKey;
            rdoComboKey.Checked = !loadedSingleKey;
            chkCtrl.Checked = loadedCtrl;
            chkAlt.Checked = loadedAlt;
            chkShift.Checked = loadedShift;

            if (cmbKey.Items.Contains(loadedKey)) cmbKey.SelectedItem = loadedKey;
            else cmbKey.SelectedItem = "Pause";

            UpdateHotkeyModeState();
            UpdatePasswordInputState();
        }
        catch
        {
            ApplyDefaultSettings();
            ShowStatusFailed("設定", "無法讀取登錄設定。");
        }
        finally
        {
            isLoadingSettings = false;
        }
    }

    private void ApplyDefaultSettings()
    {
        chkStartMinimized.Checked = false;
        chkAutoStart.Checked = false;
        chkKeepAwake.Checked = false;
        chkMinimizeToTray.Checked = true;
        chkPasswordProtection.Checked = false;
        txtUnlockPassword.Text = "";
        rdoSingleKey.Checked = true;
        rdoComboKey.Checked = false;
        chkCtrl.Checked = false;
        chkAlt.Checked = false;
        chkShift.Checked = false;
        cmbKey.SelectedItem = "Pause";
        UpdateHotkeyModeState();
        UpdatePasswordInputState();
    }

    private void SaveSettings()
    {
        try
        {
            using (RegistryKey settingsKey = Registry.CurrentUser.CreateSubKey(SETTINGS_KEY_PATH))
            {
                if (settingsKey == null) throw new InvalidOperationException("Unable to create settings key.");

                WriteRegistryBoolean(settingsKey, "StartMinimized", chkStartMinimized != null && chkStartMinimized.Checked);
                WriteRegistryBoolean(settingsKey, "AutoStart", chkAutoStart != null && chkAutoStart.Checked);
                WriteRegistryBoolean(settingsKey, "KeepAwake", chkKeepAwake != null && chkKeepAwake.Checked);
                WriteRegistryBoolean(settingsKey, "MinimizeToTray", chkMinimizeToTray != null && chkMinimizeToTray.Checked);
                WriteRegistryBoolean(settingsKey, "PasswordProtection", chkPasswordProtection != null && chkPasswordProtection.Checked);
                WriteRegistryBoolean(settingsKey, "SingleKey", rdoSingleKey != null && rdoSingleKey.Checked);
                WriteRegistryBoolean(settingsKey, "Ctrl", chkCtrl != null && chkCtrl.Checked);
                WriteRegistryBoolean(settingsKey, "Alt", chkAlt != null && chkAlt.Checked);
                WriteRegistryBoolean(settingsKey, "Shift", chkShift != null && chkShift.Checked);

                settingsKey.SetValue(
                    "Key",
                    cmbKey != null && cmbKey.SelectedItem != null ? cmbKey.SelectedItem.ToString() : "Pause",
                    RegistryValueKind.String);

                string password = txtUnlockPassword != null ? txtUnlockPassword.Text : "";
                settingsKey.SetValue("UnlockPassword", ProtectPassword(password), RegistryValueKind.Binary);
            }
        }
        catch
        {
            ShowStatusFailed("設定", "無法寫入登錄設定。");
        }
    }

    private static bool ReadRegistryBoolean(RegistryKey key, string valueName, bool defaultValue)
    {
        object storedValue = key.GetValue(valueName);
        if (storedValue is int) return (int)storedValue != 0;
        return defaultValue;
    }

    private static string ReadRegistryString(RegistryKey key, string valueName, string defaultValue)
    {
        object storedValue = key.GetValue(valueName);
        return storedValue == null ? defaultValue : storedValue.ToString();
    }

    private static void WriteRegistryBoolean(RegistryKey key, string valueName, bool value)
    {
        key.SetValue(valueName, value ? 1 : 0, RegistryValueKind.DWord);
    }

    private static byte[] ProtectPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return new byte[0];

        byte[] plainBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    private static string UnprotectPassword(byte[] protectedBytes)
    {
        if (protectedBytes == null || protectedBytes.Length == 0) return "";

        byte[] plainBytes = null;
        try
        {
            plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
        finally
        {
            if (plainBytes != null) Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    private bool ApplyAutoStartSetting()
    {
        try
        {
            using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true))
            {
                if (runKey == null)
                {
                    ShowStatusFailed("隨 Windows 啟動");
                    return false;
                }

                CleanupLegacyAutoStartValues(runKey);

                if (chkAutoStart != null && chkAutoStart.Checked)
                {
                    string exePath = Application.ExecutablePath;
                    runKey.SetValue(RUN_VALUE_NAME, "\"" + exePath + "\"");
                }
                else
                {
                    if (runKey.GetValue(RUN_VALUE_NAME) != null) runKey.DeleteValue(RUN_VALUE_NAME, false);
                }
            }

            return true;
        }
        catch
        {
            ShowStatusFailed("隨 Windows 啟動");
            return false;
        }
    }

    private void CleanupLegacyAutoStartValues(RegistryKey runKey)
    {
        try
        {
            if (runKey.GetValue(LEGACY_RUN_VALUE_NAME_1) != null) runKey.DeleteValue(LEGACY_RUN_VALUE_NAME_1, false);
            if (runKey.GetValue(LEGACY_RUN_VALUE_NAME_2) != null) runKey.DeleteValue(LEGACY_RUN_VALUE_NAME_2, false);
        }
        catch
        {
        }
    }

    private void ApplyKeepAwakeSetting(bool showStatus)
    {
        if (chkKeepAwake != null && chkKeepAwake.Checked)
        {
            uint result = RefreshKeepAwakeState(showStatus);
            if (result != 0 && keepAwakeRefreshTimer != null && !keepAwakeRefreshTimer.Enabled) keepAwakeRefreshTimer.Start();
            return;
        }

        if (keepAwakeRefreshTimer != null && keepAwakeRefreshTimer.Enabled) keepAwakeRefreshTimer.Stop();

        try
        {
            SetThreadExecutionState(ES_CONTINUOUS);
            if (showStatus) ShowStatusCancelled("工作模式");
        }
        catch
        {
            if (showStatus) ShowStatusFailed("工作模式");
        }
    }

    private uint RefreshKeepAwakeState(bool showStatus)
    {
        try
        {
            uint result = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            if (showStatus)
            {
                if (result != 0) ShowStatusEnabled("工作模式");
                else ShowStatusFailed("工作模式");
            }
            return result;
        }
        catch
        {
            if (showStatus) ShowStatusFailed("工作模式");
            return 0;
        }
    }

    private void RestorePowerState()
    {
        try
        {
            if (keepAwakeRefreshTimer != null && keepAwakeRefreshTimer.Enabled) keepAwakeRefreshTimer.Stop();
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch
        {
        }
    }

    private void ApplyHotkey()
    {
        UpdateHotkeyModeState();
        int modifiers = 0;

        if (!useSingleKey)
        {
            if (chkAlt.Checked) modifiers |= MOD_ALT;
            if (chkCtrl.Checked) modifiers |= MOD_CONTROL;
            if (chkShift.Checked) modifiers |= MOD_SHIFT;
        }

        Keys selectedKey = ParseSelectedKey();

        if (IsDiscouragedHotkey(modifiers, selectedKey))
        {
            ShowStatusFailed("快捷鍵", "此組合其他程式已使用。");
            UpdateUI();
            return;
        }

        UnregisterCurrentHotkey();

        currentModifiers = modifiers;
        currentDisplayKey = selectedKey;
        currentKey = NormalizeHotkeyKey(selectedKey, modifiers);

        bool ok = false;
        if (hotkeyWindow != null)
        {
            int registerModifiers = currentModifiers | MOD_NOREPEAT;
            ok = hotkeyWindow.RegisterHotkey(HOTKEY_ID, registerModifiers, (int)currentKey);
        }

        hotkeyRegistered = ok;
        if (ok) ShowStatusEnabled("快捷鍵");
        else ShowStatusFailed("快捷鍵", "此組合其他程式已使用。");

        ApplyAutoStartSetting();
        ApplyKeepAwakeSetting(false);
        SaveSettings();
        UpdateUI();
    }

    private bool IsDiscouragedHotkey(int modifiers, Keys selectedKey)
    {
        bool hasCtrl = (modifiers & MOD_CONTROL) != 0;
        bool hasAlt = (modifiers & MOD_ALT) != 0;
        bool hasShift = (modifiers & MOD_SHIFT) != 0;

        if (hasCtrl && hasAlt && selectedKey == Keys.Delete) return true;
        if (hasAlt && selectedKey == Keys.F4) return true;
        if (hasAlt && selectedKey == Keys.Tab) return true;
        if (hasCtrl && selectedKey == Keys.Pause) return true;
        if ((hasCtrl && selectedKey == Keys.Insert) || (hasShift && selectedKey == Keys.Insert)) return true;
        if (hasCtrl && hasAlt && (selectedKey == Keys.End || selectedKey == Keys.Insert || selectedKey == Keys.Pause || selectedKey == Keys.Delete)) return true;
        return false;
    }

    private string BuildHotkeyText(int modifiers, Keys key)
    {
        StringBuilder text = new StringBuilder();
        if ((modifiers & MOD_CONTROL) != 0) text.Append("Ctrl + ");
        if ((modifiers & MOD_ALT) != 0) text.Append("Alt + ");
        if ((modifiers & MOD_SHIFT) != 0) text.Append("Shift + ");
        text.Append(GetFriendlyKeyName(key));
        return text.ToString();
    }

    private string GetFriendlyKeyName(Keys key)
    {
        Keys keyCode = key & Keys.KeyCode;
        if (keyCode == Keys.Prior) return "PageUp";
        if (keyCode == Keys.Next) return "PageDown";
        return keyCode.ToString();
    }

    private Keys NormalizeHotkeyKey(Keys selectedKey, int modifiers)
    {
        if (selectedKey == Keys.Pause && (modifiers & MOD_CONTROL) != 0) return Keys.Cancel;
        return selectedKey;
    }

    private Keys ParseSelectedKey()
    {
        if (cmbKey.SelectedItem == null) return Keys.Pause;
        try
        {
            return (Keys)Enum.Parse(typeof(Keys), cmbKey.SelectedItem.ToString());
        }
        catch
        {
            return Keys.Pause;
        }
    }

    private void UnregisterCurrentHotkey()
    {
        if (hotkeyRegistered && hotkeyWindow != null)
        {
            hotkeyWindow.UnregisterHotkey(HOTKEY_ID);
            hotkeyRegistered = false;
        }
    }

    private void ExecuteHotkeyAction()
    {
        ToggleBlackOverlay();
    }

    private void ToggleBlackOverlay()
    {
        if (overlayForm == null || overlayForm.IsDisposed)
        {
            overlayForm = new LowRiskBlackOverlayForm();
            overlayForm.PasswordAccepted += delegate { HideBlackOverlayAfterPassword(); };
        }

        if (overlayForm.Visible)
        {
            if (overlayForm.IsPasswordProtectionEnabled)
            {
                overlayForm.RequestPasswordUnlock();
                ShowTemporaryStatus("「密碼保護」請輸入解除密碼", SystemColors.ControlText);
                UpdateUI();
                return;
            }

            overlayForm.Hide();
            ShowStatusCancelled("螢幕保護");
            UpdateUI();
            return;
        }

        bool passwordEnabled = chkPasswordProtection != null && chkPasswordProtection.Checked && txtUnlockPassword != null && txtUnlockPassword.Text.Length > 0;
        string password = txtUnlockPassword != null ? txtUnlockPassword.Text : "";

        overlayForm.SetPasswordOptions(passwordEnabled, password);
        overlayForm.ShowBlackOverlay();
        ShowStatusEnabled("螢幕保護");
        UpdateUI();
    }

    private void HideBlackOverlayAfterPassword()
    {
        if (overlayForm != null && !overlayForm.IsDisposed) overlayForm.Hide();
        ShowStatusCancelled("螢幕保護");
        UpdateUI();
    }

    private void ShowStatusEnabled(string optionName)
    {
        ShowTemporaryStatus("「" + optionName + "」✔ 已啟動", Color.SeaGreen);
    }

    private void ShowStatusCancelled(string optionName)
    {
        ShowTemporaryStatus("「" + optionName + "」❌ 已取消", Color.Firebrick);
    }

    private void ShowStatusFailed(string optionName)
    {
        ShowTemporaryStatus("「" + optionName + "」❗ 設定失敗", Color.OrangeRed);
    }

    private void ShowStatusFailed(string optionName, string detail)
    {
        if (detail == null || detail.Length == 0)
        {
            ShowStatusFailed(optionName);
            return;
        }
        ShowTemporaryStatus("「" + optionName + "」❗ 設定失敗，" + detail, Color.OrangeRed);
    }

    private void ShowTemporaryStatus(string message)
    {
        ShowTemporaryStatus(message, SystemColors.ControlText);
    }

    private void ShowTemporaryStatus(string message, Color color)
    {
        temporaryStatusMessage = message;
        temporaryStatusColor = color;
        temporaryStatusUntil = DateTime.Now.AddSeconds(3);
    }

    private string GetCurrentStatusText()
    {
        if (DateTime.Now < temporaryStatusUntil && temporaryStatusMessage != null && temporaryStatusMessage.Length > 0) return temporaryStatusMessage;
        return "٩(｡・ω・｡)و  執行中";
    }

    private Color GetCurrentStatusColor()
    {
        if (DateTime.Now < temporaryStatusUntil && temporaryStatusMessage != null && temporaryStatusMessage.Length > 0) return temporaryStatusColor;
        return Color.MediumPurple;
    }

    private void UpdateUI()
    {
        if (lblStatusValue != null)
        {
            lblStatusValue.Text = GetCurrentStatusText();
            lblStatusValue.ForeColor = GetCurrentStatusColor();
        }

        if (lblHotkeyValue != null) lblHotkeyValue.Text = GetHotkeyText();
        if (trayIcon != null) trayIcon.Text = APP_NAME + " - 執行中";
    }

    private string GetHotkeyText()
    {
        return BuildHotkeyText(currentModifiers, currentDisplayKey);
    }

    private void HideToTray()
    {
        internalTrayMinimize = true;
        Hide();
        ShowInTaskbar = false;
        internalTrayMinimize = false;
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void CloseProgram()
    {
        Close();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && chkMinimizeToTray != null && chkMinimizeToTray.Checked && !internalTrayMinimize)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        UnregisterCurrentHotkey();
        RestorePowerState();

        if (trayIcon != null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        if (statusTimer != null)
        {
            statusTimer.Stop();
            statusTimer.Dispose();
            statusTimer = null;
        }

        if (keepAwakeRefreshTimer != null)
        {
            keepAwakeRefreshTimer.Stop();
            keepAwakeRefreshTimer.Dispose();
            keepAwakeRefreshTimer = null;
        }

        if (hotkeyWindow != null)
        {
            hotkeyWindow.Dispose();
            hotkeyWindow = null;
        }

        if (overlayForm != null && !overlayForm.IsDisposed)
        {
            overlayForm.Close();
            overlayForm = null;
        }

        base.OnFormClosing(e);
    }

    private static Icon GetApplicationIcon()
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream iconStream = asm.GetManifestResourceStream("EmbeddedIcon.ico"))
            {
                if (iconStream != null) return new Icon(iconStream);
            }
            return SystemIcons.Shield;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}

public class HotkeyMessageWindow : NativeWindow, IDisposable
{
    public event EventHandler HotkeyPressed;
    private const int WM_HOTKEY = 0x0312;

    public HotkeyMessageWindow()
    {
        CreateParams cp = new CreateParams();
        cp.Caption = "OwO Screen Veil Hotkey Message Window";
        CreateHandle(cp);
    }

    public bool RegisterHotkey(int id, int modifiers, int key)
    {
        return RegisterHotKey(Handle, id, modifiers, key);
    }

    public void UnregisterHotkey(int id)
    {
        UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && HotkeyPressed != null) HotkeyPressed(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public class LowRiskBlackOverlayForm : Form
{
    public event EventHandler PasswordAccepted;

    private OverlayPasswordInputBox overlayPasswordBox;
    private Timer overlayWatchdogTimer;
    private Timer winKeyMonitorTimer;
    private Cursor transparentCursor;

    private bool systemDisplayEventSubscribed = false;
    private bool passwordProtectionEnabled = false;
    private bool isReassertingForeground = false;

    private string unlockPassword = "";
    private DateTime lastForegroundReassertTime = DateTime.MinValue;
    private DateTime lastWinKeyRecoveryTime = DateTime.MinValue;

    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int OVERLAY_WATCHDOG_INTERVAL_MS = 80;
    private const int WIN_KEY_MONITOR_INTERVAL_MS = 25;
    private const int WIN_KEY_RECOVERY_COOLDOWN_MS = 120;
    private const int MIN_FOREGROUND_REASSERT_INTERVAL_MS = 100;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int SW_SHOW = 5;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RWIN = 0x5C;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public bool IsPasswordProtectionEnabled
    {
        get { return passwordProtectionEnabled; }
    }

    public LowRiskBlackOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;

        transparentCursor = CreateTransparentCursor();
        Cursor = transparentCursor;

        overlayPasswordBox = new OverlayPasswordInputBox();
        overlayPasswordBox.SetBounds(0, 0, 300, 40);
        overlayPasswordBox.Visible = false;
        overlayPasswordBox.PasswordSubmitted += delegate { TryUnlock(); };
        Controls.Add(overlayPasswordBox);

        overlayWatchdogTimer = new Timer();
        overlayWatchdogTimer.Interval = OVERLAY_WATCHDOG_INTERVAL_MS;
        overlayWatchdogTimer.Tick += delegate
        {
            if (Visible) ReassertOverlayForeground(false);
        };

        winKeyMonitorTimer = new Timer();
        winKeyMonitorTimer.Interval = WIN_KEY_MONITOR_INTERVAL_MS;
        winKeyMonitorTimer.Tick += delegate
        {
            if (Visible) CheckWinKeyRecoveryOnlyWhenBlackScreen();
        };
    }

    public void SetPasswordOptions(bool enabled, string password)
    {
        unlockPassword = password == null ? "" : password;
        passwordProtectionEnabled = enabled && unlockPassword.Length > 0;

        overlayPasswordBox.Text = "";
        overlayPasswordBox.Visible = passwordProtectionEnabled;
        overlayPasswordBox.SetErrorState(false);
        Invalidate();
    }

    public void RequestPasswordUnlock()
    {
        if (!passwordProtectionEnabled) return;

        overlayPasswordBox.Visible = true;
        overlayPasswordBox.Text = "";
        overlayPasswordBox.SetErrorState(false);

        ReassertOverlayForeground(true);
        overlayPasswordBox.Focus();
        Invalidate();
    }

    public void ShowBlackOverlay()
    {
        TopMost = true;
        ShowInTaskbar = false;
        RefreshOverlayBoundsOnly();

        overlayPasswordBox.Text = "";
        overlayPasswordBox.SetErrorState(false);
        overlayPasswordBox.Visible = passwordProtectionEnabled;

        Show();

        SubscribeDisplaySettingsChanged();
        StartOverlayWatchdog();
        StartWinKeyMonitorOnlyWhenBlackScreen();
        ReassertOverlayForeground(true);

        if (passwordProtectionEnabled) overlayPasswordBox.Focus();
        else Focus();

        Invalidate();
    }

    private void TryUnlock()
    {
        if (!passwordProtectionEnabled) return;

        if (overlayPasswordBox.Text == unlockPassword)
        {
            overlayPasswordBox.Text = "";
            overlayPasswordBox.SetErrorState(false);
            if (PasswordAccepted != null) PasswordAccepted(this, EventArgs.Empty);
        }
        else
        {
            overlayPasswordBox.Text = "";
            overlayPasswordBox.SetErrorState(true);
            overlayPasswordBox.Focus();
            Invalidate();
        }
    }

    private void RefreshOverlayBoundsOnly()
    {
        if (IsDisposed) return;

        Rectangle currentVirtualScreen = SystemInformation.VirtualScreen;
        if (Bounds != currentVirtualScreen) Bounds = currentVirtualScreen;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
    }

    private void ReassertOverlayForeground(bool force)
    {
        if (IsDisposed || !Visible) return;
        if (isReassertingForeground) return;

        if (!force)
        {
            double elapsedMilliseconds = (DateTime.Now - lastForegroundReassertTime).TotalMilliseconds;
            if (elapsedMilliseconds < MIN_FOREGROUND_REASSERT_INTERVAL_MS) return;
        }

        isReassertingForeground = true;
        lastForegroundReassertTime = DateTime.Now;

        try
        {
            RefreshOverlayBoundsOnly();
            ShowWindow(Handle, SW_SHOW);
            CenterPasswordBoxOnPrimaryScreen();

            if (!TopMost) TopMost = true;

            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringToFront();
            Activate();
            SetForegroundWindow(Handle);

            if (passwordProtectionEnabled && overlayPasswordBox.Visible) overlayPasswordBox.Focus();
            else Focus();

            Invalidate();
        }
        catch
        {
        }
        finally
        {
            isReassertingForeground = false;
        }
    }

    private void CenterPasswordBoxOnPrimaryScreen()
    {
        Rectangle primaryScreenBounds = Screen.PrimaryScreen.Bounds;
        int relativeX = primaryScreenBounds.X - Bounds.X;
        int relativeY = primaryScreenBounds.Y - Bounds.Y;
        int x = relativeX + primaryScreenBounds.Width / 2 - overlayPasswordBox.Width / 2;
        int y = relativeY + primaryScreenBounds.Height / 2 - overlayPasswordBox.Height / 2 - 12;
        overlayPasswordBox.Location = new Point(x, y);
    }

    private void CheckWinKeyRecoveryOnlyWhenBlackScreen()
    {
        if (!Visible || IsDisposed) return;
        if (!IsWinKeyCurrentlyDown()) return;

        double elapsedMilliseconds = (DateTime.Now - lastWinKeyRecoveryTime).TotalMilliseconds;
        if (elapsedMilliseconds < WIN_KEY_RECOVERY_COOLDOWN_MS) return;

        lastWinKeyRecoveryTime = DateTime.Now;

        try
        {
            SendEscTap();
            ReassertOverlayForeground(true);
        }
        catch
        {
            ReassertOverlayForeground(true);
        }
    }

    private bool IsWinKeyCurrentlyDown()
    {
        try
        {
            return (GetAsyncKeyState(VK_LWIN) & unchecked((short)0x8000)) != 0 ||
                   (GetAsyncKeyState(VK_RWIN) & unchecked((short)0x8000)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void SendEscTap()
    {
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private Cursor CreateTransparentCursor()
    {
        Bitmap bitmap = new Bitmap(16, 16);
        using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Transparent);

        IntPtr iconHandle = bitmap.GetHicon();
        IconInfo iconInfo = new IconInfo();
        GetIconInfo(iconHandle, ref iconInfo);

        iconInfo.fIcon = false;
        iconInfo.xHotspot = 0;
        iconInfo.yHotspot = 0;

        IntPtr cursorHandle = CreateIconIndirect(ref iconInfo);

        if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
        if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);

        DestroyIcon(iconHandle);
        bitmap.Dispose();

        return new Cursor(cursorHandle);
    }

    private void SubscribeDisplaySettingsChanged()
    {
        if (systemDisplayEventSubscribed) return;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        systemDisplayEventSubscribed = true;
    }

    private void UnsubscribeDisplaySettingsChanged()
    {
        if (!systemDisplayEventSubscribed) return;
        try { SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged; }
        catch { }
        systemDisplayEventSubscribed = false;
    }

    private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
    {
        if (Visible) BeginInvokeIfPossible(delegate { ReassertOverlayForeground(true); });
    }

    private void BeginInvokeIfPossible(MethodInvoker action)
    {
        try
        {
            if (IsDisposed) return;
            if (IsHandleCreated) BeginInvoke(action);
        }
        catch
        {
        }
    }

    private void StartOverlayWatchdog()
    {
        if (overlayWatchdogTimer != null && !overlayWatchdogTimer.Enabled) overlayWatchdogTimer.Start();
    }

    private void StopOverlayWatchdog()
    {
        if (overlayWatchdogTimer != null && overlayWatchdogTimer.Enabled) overlayWatchdogTimer.Stop();
    }

    private void StartWinKeyMonitorOnlyWhenBlackScreen()
    {
        if (winKeyMonitorTimer != null && !winKeyMonitorTimer.Enabled) winKeyMonitorTimer.Start();
    }

    private void StopWinKeyMonitor()
    {
        if (winKeyMonitorTimer != null && winKeyMonitorTimer.Enabled) winKeyMonitorTimer.Stop();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE) BeginInvokeIfPossible(delegate { ReassertOverlayForeground(true); });
        base.WndProc(ref m);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible) ReassertOverlayForeground(true);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (Visible) ReassertOverlayForeground(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!passwordProtectionEnabled || !overlayPasswordBox.Visible) return;

        string hintText = "Backspace = 刪除 / Delete = 清除 / Enter = 確認";
        Rectangle hintRect = new Rectangle(0, overlayPasswordBox.Bottom + 14, Width, 30);
        TextRenderer.DrawText(
            e.Graphics,
            hintText,
            Font,
            hintRect,
            Color.Gray,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (passwordProtectionEnabled)
        {
            if (!overlayPasswordBox.Focused) overlayPasswordBox.Focus();
            return;
        }
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!passwordProtectionEnabled) e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (passwordProtectionEnabled) overlayPasswordBox.Focus();
        else Focus();
        ReassertOverlayForeground(true);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            StopOverlayWatchdog();
            StopWinKeyMonitor();
            UnsubscribeDisplaySettingsChanged();
            overlayPasswordBox.Text = "";
            overlayPasswordBox.Visible = false;
            overlayPasswordBox.SetErrorState(false);
        }
        else
        {
            ReassertOverlayForeground(true);
        }
        Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopOverlayWatchdog();
        StopWinKeyMonitor();
        UnsubscribeDisplaySettingsChanged();

        if (winKeyMonitorTimer != null)
        {
            winKeyMonitorTimer.Stop();
            winKeyMonitorTimer.Dispose();
            winKeyMonitorTimer = null;
        }

        if (overlayWatchdogTimer != null)
        {
            overlayWatchdogTimer.Stop();
            overlayWatchdogTimer.Dispose();
            overlayWatchdogTimer = null;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopOverlayWatchdog();
            StopWinKeyMonitor();

            if (winKeyMonitorTimer != null)
            {
                winKeyMonitorTimer.Stop();
                winKeyMonitorTimer.Dispose();
                winKeyMonitorTimer = null;
            }

            if (overlayWatchdogTimer != null)
            {
                overlayWatchdogTimer.Stop();
                overlayWatchdogTimer.Dispose();
                overlayWatchdogTimer = null;
            }

            if (transparentCursor != null)
            {
                transparentCursor.Dispose();
                transparentCursor = null;
            }
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, ref IconInfo piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}

public class CenteredPasswordTextBox : Control
{
    private string innerText = "";
    private string placeholderText = "";
    private char passwordChar = '●';

    public CenteredPasswordTextBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        Height = 24;
        TabStop = true;
        Cursor = Cursors.IBeam;
    }

    public override string Text
    {
        get { return innerText; }
        set
        {
            string newValue = value == null ? "" : value;
            if (innerText != newValue)
            {
                innerText = newValue;
                OnTextChanged(EventArgs.Empty);
            }
            Invalidate();
        }
    }

    public string PlaceholderText
    {
        get { return placeholderText; }
        set
        {
            placeholderText = value == null ? "" : value;
            Invalidate();
        }
    }

    public char PasswordChar
    {
        get { return passwordChar; }
        set
        {
            passwordChar = value;
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Color backColor = Enabled ? SystemColors.Window : SystemColors.Control;
        Color borderColor = Focused ? SystemColors.Highlight : SystemColors.ControlDark;
        Color textColor = Enabled ? ForeColor : SystemColors.GrayText;

        using (SolidBrush brush = new SolidBrush(backColor)) e.Graphics.FillRectangle(brush, ClientRectangle);
        Rectangle borderRect = new Rectangle(0, 0, Width - 1, Height - 1);
        ControlPaint.DrawBorder(e.Graphics, borderRect, borderColor, ButtonBorderStyle.Solid);

        Rectangle textRect = new Rectangle(2, 0, Width - 4, Height);
        string displayText;
        if (innerText.Length > 0) displayText = new string(passwordChar, innerText.Length);
        else
        {
            displayText = placeholderText;
            textColor = SystemColors.GrayText;
        }

        TextRenderer.DrawText(
            e.Graphics,
            displayText,
            Font,
            textRect,
            textColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!Enabled)
        {
            e.Handled = true;
            return;
        }

        if (!char.IsControl(e.KeyChar))
        {
            innerText += e.KeyChar;
            e.Handled = true;
            OnTextChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
        {
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Back)
        {
            if (innerText.Length > 0)
            {
                innerText = innerText.Substring(0, innerText.Length - 1);
                OnTextChanged(EventArgs.Empty);
            }
            e.Handled = true;
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            if (innerText.Length > 0)
            {
                innerText = "";
                OnTextChanged(EventArgs.Empty);
            }
            e.Handled = true;
            Invalidate();
            return;
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}

public class OverlayPasswordInputBox : Control
{
    public event EventHandler PasswordSubmitted;

    private string innerText = "";
    private bool errorState = false;
    private Timer errorResetTimer;

    public OverlayPasswordInputBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Black;
        ForeColor = Color.White;
        TabStop = true;
        Cursor = Cursors.IBeam;

        errorResetTimer = new Timer();
        errorResetTimer.Interval = 700;
        errorResetTimer.Tick += delegate
        {
            errorResetTimer.Stop();
            errorState = false;
            Invalidate();
        };
    }

    public override string Text
    {
        get { return innerText; }
        set
        {
            string newValue = value == null ? "" : value;
            if (innerText != newValue)
            {
                innerText = newValue;
                OnTextChanged(EventArgs.Empty);
            }
            Invalidate();
        }
    }

    public void SetErrorState(bool enabled)
    {
        errorState = enabled;
        if (enabled)
        {
            errorResetTimer.Stop();
            errorResetTimer.Start();
        }
        else
        {
            errorResetTimer.Stop();
        }
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (SolidBrush backgroundBrush = new SolidBrush(Color.Black)) e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        Color borderColor = errorState ? Color.OrangeRed : Color.White;
        using (Pen borderPen = new Pen(borderColor)) e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        string bullets = new string('●', innerText.Length);
        TextRenderer.DrawText(
            e.Graphics,
            bullets,
            Font,
            new Rectangle(2, 0, Width - 4, Height),
            Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!char.IsControl(e.KeyChar))
        {
            innerText += e.KeyChar;
            e.Handled = true;
            OnTextChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (PasswordSubmitted != null) PasswordSubmitted(this, EventArgs.Empty);
            return;
        }

        if (e.KeyCode == Keys.Back)
        {
            if (innerText.Length > 0)
            {
                innerText = innerText.Substring(0, innerText.Length - 1);
                OnTextChanged(EventArgs.Empty);
            }
            e.SuppressKeyPress = true;
            e.Handled = true;
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape)
        {
            if (innerText.Length > 0)
            {
                innerText = "";
                OnTextChanged(EventArgs.Empty);
            }
            e.SuppressKeyPress = true;
            e.Handled = true;
            Invalidate();
            return;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (errorResetTimer != null)
            {
                errorResetTimer.Stop();
                errorResetTimer.Dispose();
                errorResetTimer = null;
            }
        }
        base.Dispose(disposing);
    }
}

static class Program
{
    private const string SingleInstanceMutexName =
        @"Local\LeonOwO.OwOScreenVeil.75EBFDC4-0267-4D6A-A650-500895E788B1";

    [STAThread]
    static void Main()
    {
        bool createdNew;

        using (System.Threading.Mutex singleInstanceMutex =
            new System.Threading.Mutex(
                true,
                SingleInstanceMutexName,
                out createdNew))
        {
            if (!createdNew)
            {
                MessageBox.Show(
                    "OwO Screen Veil 已在執行中。\n請從系統匣開啟現有程式。",
                    "OwO Screen Veil",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());

            GC.KeepAlive(singleInstanceMutex);
        }
    }
}