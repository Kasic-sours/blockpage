using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AntiBlockManager.UI;

public partial class ToastNotification : Window
{
    private readonly DispatcherTimer _closeTimer;

    public ToastNotification(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        // Позиционируем в правом нижнем углу экрана
        var workArea = SystemParameters.WorkArea;
        this.Left = workArea.Right - this.Width - 25;
        this.Top = workArea.Bottom - this.Height - 25;
        this.Opacity = 0;

        // Автоматическое закрытие через 3.5 секунды
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _closeTimer.Tick += (s, e) =>
        {
            _closeTimer.Stop();
            this.Close();
        };
        _closeTimer.Start();

        Loaded += (s, e) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            this.BeginAnimation(OpacityProperty, fadeIn);
        };
    }
}