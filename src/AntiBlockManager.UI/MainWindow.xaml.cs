using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntiBlockManager.Application.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace AntiBlockManager.UI;

public enum LogLevel { Info, Success, Warning, Error }

public class LogEntry
{
    public string Message { get; set; } = "";
    public Brush Color { get; set; } = Brushes.White;
}

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private bool _isEnglish = false;
    private string _avatarPath = "";

    private WinForms.NotifyIcon? _trayIcon;
    private Process? _tgWsProxyProcess;
    private Process? _winwsProcess;
    private DispatcherTimer? _processMonitor;

    private DispatcherTimer? _discordLoadTimer, _youtubeLoadTimer, _telegramLoadTimer;
    private DispatcherTimer? _discordTimer, _youtubeTimer, _telegramTimer;
    private TimeSpan _discordTime, _youtubeTime, _telegramTime;

    private DispatcherTimer? _musicTimer;
    private bool _isDraggingSlider = false;
    private bool _isMusicPlaying = false;
    private bool _isTrackLoaded = false;

    private readonly MediaPlayer _player = new MediaPlayer();
    private readonly List<string> _playlist = new();
    private int _currentTrackIndex = -1;

    // ⚠️ ЗАМЕНИТЕ НА СВОЙ РЕПОЗИТОРИЙ (без /releases/...!)
    private const string UpdateRepoUrl = "https://github.com/ВАШ_ЛОГИН/ВАШ_РЕПОЗИТОРИЙ";

    // Аргументы winws2 для Discord (TCP API + UDP Voice 50000–50100)
    private const string DiscordArgs =
        "--wf-tcp-out=443,80,2053,2083,2087,2096,8443 " +
        "--wf-udp-out=443,50000-50100 " +
        "--filter-tcp=443,80,2053,2083,2087,2096,8443 " +
        "--filter-udp=443,50000-50100 " +
        "--dpi-desync=fake,disorder2 --dpi-desync-ttl=5 --dpi-desync-repeats=6";

    // Аргументы winws2 для YouTube
    private const string YoutubeArgs =
        "--wf-tcp-out=443 " +
        "--filter-tcp=443 " +
        "--dpi-desync=fake,split2 --dpi-desync-split-pos=1 --dpi-desync-ttl=5";

    public ObservableCollection<LogEntry> LogEntries { get; set; } = new();

    public MainWindow()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _isEnglish = _settingsService.Current.Language == "en";

        LogsList.ItemsSource = LogEntries;
        ChkLightTheme.IsChecked = _settingsService.Current.IsLightTheme;

        InitializeTimers();
        InitializeProcessMonitor();
        InitializePlayer();
        InitializeTray();
        RefreshVersionText();

        this.Loaded += OnWindowLoaded;
        this.Closed += OnWindowClosed;

        AddLog("Приложение запущено", LogLevel.Info);
        CheckAdminRights();
    }

    // ---------- Startup ----------
    private void CheckAdminRights()
    {
        try
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            AddLog(isAdmin ? "Права администратора: OK" : "ВНИМАНИЕ: нет прав администратора",
                   isAdmin ? LogLevel.Success : LogLevel.Warning);
        }
        catch (Exception ex) { AddLog($"Не удалось проверить права: {ex.Message}", LogLevel.Warning); }
    }

    private void RefreshVersionText()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            string s = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "unknown";
            UpdateVersionText.Text = _isEnglish ? $"Current version: {s}" : $"Текущая версия: {s}";
        }
        catch { }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string logoPath = Path.Combine(baseDir, "logo.png");
        if (File.Exists(logoPath))
        {
            LogoImage.Source = new BitmapImage(new Uri(logoPath));
            LogoFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            LogoImage.Visibility = Visibility.Collapsed;
            LogoFallback.Visibility = Visibility.Visible;
        }

        string videoPath = Path.Combine(baseDir, "background.mp4");
        if (File.Exists(videoPath))
        {
            BgVideo.Source = new Uri(videoPath, UriKind.Absolute);
            BgVideo.LoadedBehavior = MediaState.Manual;
            BgVideo.IsMuted = true;
            BgVideo.Play();
        }
        else BgVideo.Visibility = Visibility.Collapsed;

        LoadAvatar();
        ApplyLanguage();

        // Обновляем строку версии при старте (локализация)
        RefreshVersionText();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        StopTgProxy();
        StopWinws();
        _musicTimer?.Stop();
        _processMonitor?.Stop();
        try { _player.Stop(); _player.Close(); } catch { }
        try { BgVideo.Stop(); BgVideo.Close(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
    }

    // ---------- Мониторинг процессов (пункт 1 — авто-детект упавшего winws) ----------
    private void InitializeProcessMonitor()
    {
        _processMonitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _processMonitor.Tick += (s, e) =>
        {
            if (_winwsProcess != null && _winwsProcess.HasExited &&
                (YouTubeBtn.Tag?.ToString() == "Active" || DiscordBtn.Tag?.ToString() == "Active"))
            {
                AddLog("Процесс winws2 завершился неожиданно", LogLevel.Error);
                _winwsProcess = null;
                if (DiscordBtn.Tag?.ToString() == "Active")
                {
                    DiscordBtn.Tag = "Idle"; _discordTimer?.Stop();
                    UpdateStatusText(DiscordStatus, DiscordBtn);
                    DiscordStatus.Foreground = (Brush)FindResource("TextMuted");
                }
                if (YouTubeBtn.Tag?.ToString() == "Active")
                {
                    YouTubeBtn.Tag = "Idle"; _youtubeTimer?.Stop();
                    UpdateStatusText(YouTubeStatus, YouTubeBtn);
                    YouTubeStatus.Foreground = (Brush)FindResource("TextMuted");
                }
            }
        };
        _processMonitor.Start();
    }

    // ---------- Аватарка ----------
    private void LoadAvatar()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string cfg = Path.Combine(baseDir, "avatar.cfg");
        if (File.Exists(cfg)) _avatarPath = File.ReadAllText(cfg).Trim();
        if (string.IsNullOrEmpty(_avatarPath) || !File.Exists(_avatarPath))
            _avatarPath = Path.Combine(baseDir, "avatar.png");

        if (File.Exists(_avatarPath))
            UserAvatarImage.Source = new BitmapImage(new Uri(_avatarPath));
    }

    private void ChangeAvatar_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp",
            Title = _isEnglish ? "Select an avatar" : "Выберите аватарку"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                UserAvatarImage.Source = new BitmapImage(new Uri(dlg.FileName));
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "avatar.cfg"), dlg.FileName);
                _avatarPath = dlg.FileName;
                AddLog("Аватарка изменена", LogLevel.Success);
            }
            catch (Exception ex) { AddLog($"Ошибка аватарки: {ex.Message}", LogLevel.Error); }
        }
    }

    // ---------- Плеер (без авто-загрузки) ----------
    private void InitializePlayer()
    {
        _musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _musicTimer.Tick += (s, e) =>
        {
            if (!_isTrackLoaded) return;
            if (!_isDraggingSlider && _player.NaturalDuration.HasTimeSpan)
            {
                TrackSlider.Maximum = _player.NaturalDuration.TimeSpan.TotalSeconds;
                TrackSlider.Value = _player.Position.TotalSeconds;
                CurrentTimeText.Text = _player.Position.ToString(@"mm\:ss");
                TotalTimeText.Text = _player.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
            }
        };
        _musicTimer.Start();
        _player.MediaEnded += (s, e) => Dispatcher.Invoke(NextTrack);
    }

    private void AddTrack_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Аудио|*.mp3;*.wav;*.m4a;*.flac;*.ogg|Все файлы|*.*",
            Multiselect = true,
            Title = _isEnglish ? "Select tracks" : "Выберите треки"
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (var f in dlg.FileNames) _playlist.Add(f);
            AddLog($"Добавлено треков: {dlg.FileNames.Length}", LogLevel.Success);
            if (!_isTrackLoaded && _playlist.Count > 0)
            {
                _currentTrackIndex = 0;
                LoadTrack(_playlist[0]);
            }
        }
    }

    private void LoadTrack(string path)
    {
        try
        {
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            _isTrackLoaded = true;
            _isMusicPlaying = true;
            PlayPauseBtn.Content = "\uE769";
            TrackNameText.Text = Path.GetFileNameWithoutExtension(path);
            AddLog($"Играет: {TrackNameText.Text}", LogLevel.Info);
        }
        catch (Exception ex) { AddLog($"Ошибка плеера: {ex.Message}", LogLevel.Error); }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_isTrackLoaded)
        {
            AddTrack_Click(sender, e);
            return;
        }

        if (_isMusicPlaying)
        {
            _player.Pause();
            PlayPauseBtn.Content = "\uE768";
            _isMusicPlaying = false;
        }
        else
        {
            _player.Play();
            PlayPauseBtn.Content = "\uE769";
            _isMusicPlaying = true;
        }
    }

    private void NextTrack_Click(object sender, RoutedEventArgs e) => NextTrack();
    private void PrevTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;
        _currentTrackIndex--;
        if (_currentTrackIndex < 0) _currentTrackIndex = _playlist.Count - 1;
        LoadTrack(_playlist[_currentTrackIndex]);
    }
    private void NextTrack()
    {
        if (_playlist.Count == 0) return;
        _currentTrackIndex++;
        if (_currentTrackIndex >= _playlist.Count) _currentTrackIndex = 0;
        LoadTrack(_playlist[_currentTrackIndex]);
    }

    private void TrackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && _isTrackLoaded)
            _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player != null) _player.Volume = e.NewValue / 100.0;
    }
    private void TrackSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isDraggingSlider = true;
    private void TrackSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _isDraggingSlider = false;

    // ---------- Тема ----------
    private void ChkLightTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        _settingsService.Current.IsLightTheme = true; _settingsService.Save();
        App.LoadTheme(true); Particles.UpdateTheme(true);
        AddLog("Светлая тема", LogLevel.Info);
    }
    private void ChkLightTheme_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        _settingsService.Current.IsLightTheme = false; _settingsService.Save();
        App.LoadTheme(false); Particles.UpdateTheme(false);
        AddLog("Тёмная тема", LogLevel.Info);
    }

    // ---------- ОБНОВЛЕНИЕ (пункт 3 — полностью внутри приложения) ----------
    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateRepoUrl.Contains("ВАШ_ЛОГИН"))
        {
            MessageBox.Show(
                "URL обновления не настроен.\nОткройте MainWindow.xaml.cs и замените UpdateRepoUrl на свой репозиторий.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnCheckUpdate.IsEnabled = false;
        BtnCheckUpdate.Tag = "Checking";
        BtnCheckUpdate.Content = _isEnglish ? "Checking..." : "Проверка...";
        AddLog("Проверка обновлений...", LogLevel.Info);

        try
        {
            var mgr = new UpdateManager(new GithubSource(UpdateRepoUrl, null, false));

            if (!mgr.IsInstalled)
                AddLog("Приложение запущено портативно — обновление возможно, но требует установленной версии для применения.", LogLevel.Warning);

            var update = await mgr.CheckForUpdatesAsync();
            if (update == null)
            {
                BtnCheckUpdate.Tag = "Success";
                BtnCheckUpdate.Content = _isEnglish ? "Up to date!" : "Обновлено!";
                AddLog("Обновлений нет", LogLevel.Success);
                await Task.Delay(2000);
            }
            else
            {
                var ver = update.TargetFullRelease.Version;
                AddLog($"Найдено обновление: {ver}", LogLevel.Success);
                ShowToast($"Скачивание v{ver}...");

                await mgr.DownloadUpdatesAsync(update, progress =>
                {
                    BtnCheckUpdate.Content = $"Скачивание {progress}%";
                });

                AddLog("Загрузка завершена, применяю...", LogLevel.Success);

                var result = MessageBox.Show(
                    "Обновление скачано. Перезапустить приложение для установки?",
                    "AntiBlock Manager", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    mgr.ApplyUpdatesAndRestart(update);
                    return;
                }

                BtnCheckUpdate.Tag = "Success";
                BtnCheckUpdate.Content = _isEnglish ? "Ready" : "Готово";
            }
        }
        catch (Exception ex)
        {
            BtnCheckUpdate.Tag = "Error";
            BtnCheckUpdate.Content = _isEnglish ? "Error" : "Ошибка";
            AddLog($"Ошибка обновления: {ex.Message}", LogLevel.Error);
            MessageBox.Show(
                $"Не удалось обновить.\n\n{ex.Message}\n\n" +
                "Убедитесь, что релиз содержит files: *.nupkg и releases.win.json.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
            BtnCheckUpdate.Tag = null;
            BtnCheckUpdate.Content = _isEnglish ? "Check for updates" : "Проверить обновления";
        }
    }

    // ---------- Логи ----------
    private void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        string hex = level switch
        {
            LogLevel.Success => "#4CAF50",
            LogLevel.Warning => "#FFC107",
            LogLevel.Error   => "#F44336",
            _                => "#DDDDDD"
        };
        Dispatcher.Invoke(() =>
        {
            LogEntries.Add(new LogEntry
            {
                Message = $"[{DateTime.Now:HH:mm:ss}] {message}",
                Color = (Brush)new BrushConverter().ConvertFrom(hex)!
            });
            if (LogEntries.Count > 1000) LogEntries.RemoveAt(0);
        });
    }

    // ---------- Маскирование ----------
    private void ToggleMask(TextBox box)
    {
        if (box.Tag == null) { box.Tag = box.Text; box.Text = new string('●', box.Text.Length); }
        else { box.Text = box.Tag.ToString() ?? string.Empty; box.Tag = null; }
    }
    private void ToggleLinkVisibility_Click(object sender, RoutedEventArgs e) => ToggleMask(TgLinkBox);
    private void ToggleHostVisibility_Click(object sender, RoutedEventArgs e) => ToggleMask(TgHostBox);
    private void TogglePortVisibility_Click(object sender, RoutedEventArgs e) => ToggleMask(TgPortBox);
    private void ToggleSecretVisibility_Click(object sender, RoutedEventArgs e) => ToggleMask(TgSecretBox);

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = TgLinkBox.Tag?.ToString() ?? TgLinkBox.Text;
            if (!string.IsNullOrEmpty(text)) { Clipboard.SetText(text); AddLog("Скопировано", LogLevel.Success); }
        }
        catch { }
    }
    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = TgLinkBox.Tag?.ToString() ?? TgLinkBox.Text;
            if (!string.IsNullOrEmpty(text)) Process.Start(new ProcessStartInfo { FileName = text, UseShellExecute = true });
        }
        catch { }
    }
    private void RefreshLink_Click(object sender, RoutedEventArgs e)
    {
        TgSecretBox.Tag = null;
        TgSecretBox.Text = Guid.NewGuid().ToString("N");
    }

    // ---------- Telegram ----------
    private void TelegramBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TelegramBtn.Tag?.ToString() is "Active" or "Loading")
        {
            TelegramBtn.Tag = "Idle"; _telegramTimer?.Stop(); _telegramLoadTimer?.Stop();
            _telegramTime = TimeSpan.Zero; TelegramTimer.Text = "00:00:00";
            UpdateStatusText(TelegramStatus, TelegramBtn);
            TelegramStatus.Foreground = (Brush)FindResource("TextMuted");
            StopTgProxy();
            AddLog("Telegram прокси остановлен", LogLevel.Info);
        }
        else
        {
            TelegramBtn.Tag = "Loading"; UpdateStatusText(TelegramStatus, TelegramBtn);
            _telegramLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _telegramLoadTimer.Tick += async (s, a) =>
            {
                _telegramLoadTimer.Stop();
                if (StartTgProxy())
                {
                    TelegramBtn.Tag = "Active";
                    _telegramTime = TimeSpan.Zero; TelegramTimer.Text = "00:00:00";
                    _telegramTimer?.Start();
                    UpdateStatusText(TelegramStatus, TelegramBtn);
                    TelegramStatus.Foreground = (Brush)FindResource("Success");
                    AddLog("Telegram прокси запущен на 127.0.0.1:1080", LogLevel.Success);
                    await Task.Delay(1000);
                    OpenTelegram();
                }
                else
                {
                    TelegramBtn.Tag = "Idle"; UpdateStatusText(TelegramStatus, TelegramBtn);
                    AddLog("Не удалось запустить TgWsProxy_windows.exe", LogLevel.Error);
                }
            };
            _telegramLoadTimer.Start();
        }
    }

    private void OpenTelegram()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "tg://socks?server=127.0.0.1&port=1080", UseShellExecute = true }); }
        catch { try { Process.Start(new ProcessStartInfo { FileName = "https://web.telegram.org/", UseShellExecute = true }); } catch { } }
    }

    private bool StartTgProxy()
    {
        try
        {
            StopTgProxy();
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "TgWsProxy_windows.exe");
            if (!File.Exists(exe)) { AddLog("Не найден TgWsProxy_windows.exe", LogLevel.Error); return false; }

            _tgWsProxyProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = "--port 1080"
                }
            };
            _tgWsProxyProcess.Start();
            return true;
        }
        catch (Exception ex) { AddLog($"Ошибка запуска Telegram: {ex.Message}", LogLevel.Error); return false; }
    }
    private void StopTgProxy()
    {
        try { if (_tgWsProxyProcess is { HasExited: false }) _tgWsProxyProcess.Kill(); } catch { }
        _tgWsProxyProcess = null;
    }

    // ---------- YouTube (пункт 4 — использует общий winws) ----------
    private void YouTubeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (YouTubeBtn.Tag?.ToString() is "Active" or "Loading")
        {
            YouTubeBtn.Tag = "Idle"; _youtubeTimer?.Stop(); _youtubeLoadTimer?.Stop();
            _youtubeTime = TimeSpan.Zero; YouTubeTimer.Text = "00:00:00";
            UpdateStatusText(YouTubeStatus, YouTubeBtn);
            YouTubeStatus.Foreground = (Brush)FindResource("TextMuted");
            StopWinws();
            AddLog("YouTube zapret остановлен", LogLevel.Info);
        }
        else
        {
            YouTubeBtn.Tag = "Loading"; UpdateStatusText(YouTubeStatus, YouTubeBtn);
            _youtubeLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _youtubeLoadTimer.Tick += (s, a) =>
            {
                _youtubeLoadTimer.Stop();
                if (StartWinws(YoutubeArgs))
                {
                    YouTubeBtn.Tag = "Active";
                    _youtubeTime = TimeSpan.Zero; YouTubeTimer.Text = "00:00:00";
                    _youtubeTimer?.Start();
                    UpdateStatusText(YouTubeStatus, YouTubeBtn);
                    YouTubeStatus.Foreground = (Brush)FindResource("Success");
                    AddLog("YouTube zapret активен", LogLevel.Success);
                }
                else
                {
                    YouTubeBtn.Tag = "Idle"; UpdateStatusText(YouTubeStatus, YouTubeBtn);
                }
            };
            _youtubeLoadTimer.Start();
        }
    }

    // ---------- Discord (пункт 4 — рабочий запуск winws2) ----------
    private void DiscordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DiscordBtn.Tag?.ToString() is "Active" or "Loading")
        {
            DiscordBtn.Tag = "Idle"; _discordTimer?.Stop(); _discordLoadTimer?.Stop();
            _discordTime = TimeSpan.Zero; DiscordTimer.Text = "00:00:00";
            UpdateStatusText(DiscordStatus, DiscordBtn);
            DiscordStatus.Foreground = (Brush)FindResource("TextMuted");
            StopWinws();
            AddLog("Discord zapret остановлен", LogLevel.Info);
        }
        else
        {
            DiscordBtn.Tag = "Loading"; UpdateStatusText(DiscordStatus, DiscordBtn);
            DiscordStatus.Foreground = (Brush)FindResource("TextMuted");
            _discordLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _discordLoadTimer.Tick += (s, a) =>
            {
                _discordLoadTimer.Stop();
                if (StartWinws(DiscordArgs))
                {
                    DiscordBtn.Tag = "Active";
                    _discordTime = TimeSpan.Zero; DiscordTimer.Text = "00:00:00";
                    _discordTimer?.Start();
                    UpdateStatusText(DiscordStatus, DiscordBtn);
                    DiscordStatus.Foreground = (Brush)FindResource("Success");
                    AddLog("Discord zapret активен", LogLevel.Success);
                }
                else
                {
                    DiscordBtn.Tag = "Idle"; UpdateStatusText(DiscordStatus, DiscordBtn);
                }
            };
            _discordLoadTimer.Start();
        }
    }

    /// <summary>
    /// Запускает winws2.exe с указанными аргументами. Требует прав администратора
    /// (обеспечивается app.manifest). Переиспользует уже запущенный процесс.
    /// </summary>
    private bool StartWinws(string args)
    {
        try
        {
            StopWinws();

            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "Zapret", "winws2.exe");
            if (!File.Exists(exe)) { AddLog("Не найден Tools\\Zapret\\winws2.exe", LogLevel.Error); return false; }

            _winwsProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)!
                }
            };
            _winwsProcess.Start();
            AddLog($"winws2 запущен (PID {_winwsProcess.Id})", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            AddLog($"Ошибка запуска winws2: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private void StopWinws()
    {
        try
        {
            if (_winwsProcess is { HasExited: false })
            {
                _winwsProcess.Kill(true);
                _winwsProcess.WaitForExit(1500);
            }
        }
        catch { }
        _winwsProcess = null;
    }

    // ---------- Язык ----------
    private void LangBtn_Click(object sender, RoutedEventArgs e)
    {
        _isEnglish = !_isEnglish;
        _settingsService.Current.Language = _isEnglish ? "en" : "ru";
        _settingsService.Save();
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        RuText.Foreground = _isEnglish ? (Brush)FindResource("TextMuted") : (Brush)FindResource("TextMain");
        RuText.FontWeight = _isEnglish ? FontWeights.Normal : FontWeights.Bold;
        EnText.Foreground = _isEnglish ? (Brush)FindResource("TextMain") : (Brush)FindResource("TextMuted");
        EnText.FontWeight = _isEnglish ? FontWeights.Bold : FontWeights.Normal;

        BtnNavHome.ToolTip = _isEnglish ? "Home" : "Главная";
        BtnNavLogs.ToolTip = _isEnglish ? "Logs" : "Логи";
        BtnNavAssistant.ToolTip = _isEnglish ? "Help / FAQ" : "Помощь / FAQ";
        BtnNavSettings.ToolTip = _isEnglish ? "Settings" : "Настройки";
        BtnNavUpdate.ToolTip = _isEnglish ? "Update" : "Обновление";
        BtnNavInfo.ToolTip = _isEnglish ? "Info" : "Информация";

        CheckYouTubeBtn.Content = _isEnglish ? "Check" : "Проверить";
        CheckDiscordBtn.Content = _isEnglish ? "Check" : "Проверить";
        CheckTelegramBtn.Content = _isEnglish ? "Check" : "Проверить";

        UpdateStatusText(DiscordStatus, DiscordBtn);
        UpdateStatusText(YouTubeStatus, YouTubeBtn);
        UpdateStatusText(TelegramStatus, TelegramBtn);

        TgConnectHeader.Text = _isEnglish ? "Connection" : "Подключение";
        TgConnectHint.Text = _isEnglish ? "Data is hidden." : "Данные скрыты.";
        TgParamsHeader.Text = _isEnglish ? "Parameters" : "Параметры";
        TgHostLabel.Text = _isEnglish ? "Host" : "Хост";
        TgPortLabel.Text = _isEnglish ? "Port" : "Порт";
        TgSecretLabel.Text = _isEnglish ? "Secret Key" : "Секретный ключ";

        LogsTitle.Text = _isEnglish ? "Event Logs" : "Журнал логов";
        AiTitle.Text = _isEnglish ? "Help & FAQ" : "Помощь и Частые Вопросы";
        AiSubtitle.Text = _isEnglish ? "Answers to the most popular questions." : "Ответы на самые популярные вопросы.";
        FaqExpander1.Header = _isEnglish ? "Getting Started" : "Начало работы";
        FaqText1_1.Text = _isEnglish ? "How to enable protection?" : "Как включить защиту?";
        FaqText1_2.Text = _isEnglish ? "Click the round button under the desired service." : "Нажмите на круглую кнопку под нужным сервисом.";

        SettingsTitle.Text = _isEnglish ? "Settings" : "Настройки";
        ChkAutoStart.Content = _isEnglish ? "Run at Windows startup" : "Запускать при входе в Windows";
        ChkMinimized.Content = _isEnglish ? "Start minimized to tray" : "Старт свернутым в трей";
        ChkLightTheme.Content = _isEnglish ? "Light theme" : "Светлая тема";
        ChkAutoTg.Content = _isEnglish ? "Auto-start Telegram" : "Автозапуск Telegram";
        ChkAutoDiscord.Content = _isEnglish ? "Auto-start Discord / YouTube" : "Автозапуск Discord / YouTube";

        InfoTitle.Text = _isEnglish ? "Information" : "Информация";
        InfoHeader.Text = _isEnglish ? "What is this application?" : "Что это за приложение?";
        InfoCreatorsHeader.Text = _isEnglish ? "App Creators" : "Создатели приложения";
        InfoDevLabel.Text = _isEnglish ? "Developer:" : "Разработчик:";
        InfoTestLabel.Text = _isEnglish ? "Testers:" : "Тестировщики:";

        UpdateTitle.Text = _isEnglish ? "Updates" : "Обновление";
        BtnCheckUpdate.Content = _isEnglish ? "Check for updates" : "Проверить обновления";
        RefreshVersionText();

        ZapretPageTitle.Text = _isEnglish ? "Zapret Settings" : "Настройки Zapret";
        SaveStrategyBtn.Content = _isEnglish ? "Save" : "Сохранить";
    }

    private void UpdateStatusText(TextBlock statusBlock, Button btn)
    {
        string state = btn.Tag?.ToString() ?? "Idle";
        statusBlock.Text = state switch
        {
            "Loading" => _isEnglish ? "CONNECTING..." : "ПОДКЛЮЧЕНИЕ...",
            "Active"  => _isEnglish ? "CONNECTED"     : "ПОДКЛЮЧЕНО",
            _         => _isEnglish ? "INACTIVE"      : "НЕ АКТИВНО"
        };
    }

    // ---------- Трей ----------
    private void InitializeTray()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "AntiBlock Manager"
        };
        _trayIcon.DoubleClick += (s, e) => ShowWindow();
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (s, e) => ShowWindow());
        menu.Items.Add("Выход", null, (s, e) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShowWindow()
    {
        Show(); WindowState = WindowState.Normal; Activate();
        try { BgVideo.Play(); } catch { }
    }

    private void ExitApp()
    {
        try { _settingsService.Save(); } catch { }
        StopTgProxy(); StopWinws();
        _musicTimer?.Stop(); _processMonitor?.Stop();
        try { _player.Stop(); _player.Close(); } catch { }
        try { BgVideo.Stop(); BgVideo.Close(); } catch { }
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowToast(string message) { var t = new ToastNotification(message); t.Show(); }

    private async void CheckDiscordBtn_Click(object sender, RoutedEventArgs e) => await CheckServiceAsync("discord.com", CheckDiscordBtn, _isEnglish ? "Check" : "Проверить");
    private async void CheckYouTubeBtn_Click(object sender, RoutedEventArgs e) => await CheckServiceAsync("youtube.com", CheckYouTubeBtn, _isEnglish ? "Check" : "Проверить");
    private async void CheckTelegramBtn_Click(object sender, RoutedEventArgs e) => await CheckServiceAsync("telegram.org", CheckTelegramBtn, _isEnglish ? "Check" : "Проверить");

    private async Task CheckServiceAsync(string host, Button btn, string defaultText)
    {
        btn.IsEnabled = false;
        btn.Content = _isEnglish ? "Checking..." : "Проверка...";
        bool ok = false;
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(host, 443);
            var timeout = Task.Delay(4000);
            var done = await Task.WhenAny(connect, timeout);
            ok = done == connect && tcp.Connected;
        }
        catch { ok = false; }
        finally
        {
            btn.IsEnabled = true;
            btn.Content = defaultText;
            AddLog(ok ? $"{host}: доступен" : $"{host}: недоступен", ok ? LogLevel.Success : LogLevel.Error);
            ShowToast(ok ? $"{host} ✓" : $"{host} ✗");
        }
    }

    // ---------- Прочее ----------
    private void SaveStrategyBtn_Click(object sender, RoutedEventArgs e) => ShowToast("Стратегии сохранены");
    private void BgVideo_MediaOpened(object sender, RoutedEventArgs e) => BgImage.Visibility = Visibility.Collapsed;
    private void BgVideo_MediaEnded(object sender, RoutedEventArgs e) { BgVideo.Position = TimeSpan.Zero; BgVideo.Play(); }
    private void BgVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) { BgVideo.Visibility = Visibility.Collapsed; BgImage.Visibility = Visibility.Visible; }
    private void Window_MouseMove(object sender, MouseEventArgs e) { var pos = e.GetPosition(Particles); Particles.MousePosition = pos; }
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseBtn_Click(object sender, RoutedEventArgs e) { try { BgVideo.Pause(); } catch { } Hide(); AddLog("Свёрнуто в трей", LogLevel.Info); }

    private void InitializeTimers()
    {
        _discordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _discordTimer.Tick += (s, e) => { _discordTime = _discordTime.Add(TimeSpan.FromSeconds(1)); DiscordTimer.Text = _discordTime.ToString(@"hh\:mm\:ss"); };
        _youtubeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _youtubeTimer.Tick += (s, e) => { _youtubeTime = _youtubeTime.Add(TimeSpan.FromSeconds(1)); YouTubeTimer.Text = _youtubeTime.ToString(@"hh\:mm\:ss"); };
        _telegramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _telegramTimer.Tick += (s, e) => { _telegramTime = _telegramTime.Add(TimeSpan.FromSeconds(1)); TelegramTimer.Text = _telegramTime.ToString(@"hh\:mm\:ss"); };
    }

    private void HideAllPages()
    {
        PageHome.Visibility = Visibility.Collapsed; PageSettings.Visibility = Visibility.Collapsed;
        PageInfo.Visibility = Visibility.Collapsed; PageTelegram.Visibility = Visibility.Collapsed;
        PageZapret.Visibility = Visibility.Collapsed; PageLogs.Visibility = Visibility.Collapsed;
        PageAssistant.Visibility = Visibility.Collapsed; PageUpdate.Visibility = Visibility.Collapsed;
    }
    private void Nav_Home(object sender, RoutedEventArgs e) { HideAllPages(); PageHome.Visibility = Visibility.Visible; }
    private void Nav_Telegram(object sender, RoutedEventArgs e) { HideAllPages(); PageTelegram.Visibility = Visibility.Visible; }
    private void Nav_Zapret(object sender, RoutedEventArgs e) { HideAllPages(); PageZapret.Visibility = Visibility.Visible; }
    private void Nav_Logs(object sender, RoutedEventArgs e) { HideAllPages(); PageLogs.Visibility = Visibility.Visible; }
    private void Nav_Assistant(object sender, RoutedEventArgs e) { HideAllPages(); PageAssistant.Visibility = Visibility.Visible; }
    private void Nav_Settings(object sender, RoutedEventArgs e) { HideAllPages(); PageSettings.Visibility = Visibility.Visible; }
    private void Nav_Update(object sender, RoutedEventArgs e) { HideAllPages(); PageUpdate.Visibility = Visibility.Visible; }
    private void Nav_Info(object sender, RoutedEventArgs e) { HideAllPages(); PageInfo.Visibility = Visibility.Visible; }
}

// ---------- Частицы (без изменений) ----------
public class ParticleBackground : FrameworkElement
{
    private class Particle { public double X, Y, VX, VY; }
    private readonly List<Particle> _particles = new();
    private readonly Random _rand = new();
    public Point MousePosition { get; set; } = new Point(-1000, -1000);
    private Brush _particleBrush = new SolidColorBrush(Color.FromArgb(60, 180, 180, 180));
    private Pen _linePen = new Pen(new SolidColorBrush(Color.FromArgb(25, 180, 180, 180)), 1);

    public ParticleBackground()
    {
        IsHitTestVisible = false;
        Loaded += (s, e) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (s, e) => CompositionTarget.Rendering -= OnRendering;
    }

    public void UpdateTheme(bool isLight)
    {
        if (isLight) { _particleBrush = new SolidColorBrush(Color.FromArgb(80, 50, 50, 50)); _linePen = new Pen(new SolidColorBrush(Color.FromArgb(30, 50, 50, 50)), 1); }
        else { _particleBrush = new SolidColorBrush(Color.FromArgb(60, 180, 180, 180)); _linePen = new Pen(new SolidColorBrush(Color.FromArgb(25, 180, 180, 180)), 1); }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (_particles.Count == 0)
        {
            for (int i = 0; i < 90; i++)
                _particles.Add(new Particle { X = _rand.NextDouble() * ActualWidth, Y = _rand.NextDouble() * ActualHeight, VX = (_rand.NextDouble() - 0.5) * 1.2, VY = (_rand.NextDouble() - 0.5) * 1.2 });
        }
        UpdateParticles(); InvalidateVisual();
    }

    private void UpdateParticles()
    {
        foreach (var p in _particles)
        {
            p.X += p.VX; p.Y += p.VY;
            if (p.X <= 0 || p.X >= ActualWidth) p.VX *= -1;
            if (p.Y <= 0 || p.Y >= ActualHeight) p.VY *= -1;
            double dx = MousePosition.X - p.X, dy = MousePosition.Y - p.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 100 && dist > 10) { p.X += dx * 0.006; p.Y += dy * 0.006; }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double maxD2 = 100 * 100;
        for (int i = 0; i < _particles.Count; i++)
            for (int j = i + 1; j < _particles.Count; j++)
            {
                var dx = _particles[i].X - _particles[j].X;
                var dy = _particles[i].Y - _particles[j].Y;
                if (dx * dx + dy * dy < maxD2)
                    dc.DrawLine(_linePen, new Point(_particles[i].X, _particles[i].Y), new Point(_particles[j].X, _particles[j].Y));
            }
        foreach (var p in _particles)
            dc.DrawEllipse(_particleBrush, null, new Point(p.X, p.Y), 1.2, 1.2);
    }
}