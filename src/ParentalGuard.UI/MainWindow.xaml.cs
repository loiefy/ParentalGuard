using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ParentalGuard.UI;

/// <summary>Cửa sổ duy nhất của ứng dụng (mục 2.3 — single-instance) — chứa <see cref="RootFrame"/> cho điều hướng cấp cao (mục 4: lỗi kết nối / S1 / Main Shell).</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "ParentalGuard";
    }

    public Frame RootFrameControl => RootFrame;
}
