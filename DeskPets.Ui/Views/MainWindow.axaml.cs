using Avalonia.Controls;
using Avalonia.Input;

namespace DeskPets.Ui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnBottomRightResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }

    private void OnRightResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.East, e);
        }
    }

    private void OnBottomResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.South, e);
        }
    }
}