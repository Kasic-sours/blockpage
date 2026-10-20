using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace AntiBlockManager.UI;

public partial class LoadingScreen : Window
{
    private readonly DispatcherTimer _timer;
    private int _progress = 0;

    public LoadingScreen()
    {
        InitializeComponent();
        
        this.Opacity = 1; // Убрали постепенное появление

        this.Loaded += (s, e) => 
        {
            StartVideo();
            PlayMicroSound();
        };

        // 3 секунды = 100 шагов по 30 мс
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopMicroSound();
        base.OnClosed(e);
    }

    private void PlayMicroSound()
    {
        try
        {
            string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "micro.mp3");
            if (File.Exists(soundPath))
            {
                SplashSoundPlayer.Source = new Uri(soundPath, UriKind.Absolute);
                SplashSoundPlayer.Play();
            }
        }
        catch { }
    }

    private void StopMicroSound()
    {
        try
        {
            SplashSoundPlayer.Stop();
            SplashSoundPlayer.Close();
            SplashSoundPlayer.Source = null; 
        }
        catch { }
    }

    private void StartVideo()
    {
        try
        {
            string videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loading.mp4");
            if (File.Exists(videoPath))
            {
                BgVideo.Source = new Uri(videoPath, UriKind.Absolute);
                BgVideo.Play();
            }
        }
        catch { }
    }

    private void BgVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        try { BgVideo.Position = TimeSpan.Zero; BgVideo.Play(); } catch { }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_progress >= 100) return;
        
        _progress += 1;
        
        PercentageText.Text = $"{_progress}%";
        ProgressBarFill.Width = 3.5 * _progress;

        // На 90% (2.7 сек) глушим звук
        if (_progress == 90) StopMicroSound();

        if (_progress >= 100)
        {
            _timer.Stop();
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(100);
                try { DialogResult = true; } 
                catch { Close(); }
            });
        }
    }
}