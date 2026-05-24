using System.Windows;
using System.Windows.Controls;

namespace EqFlex.App.Controls;

public partial class ColorPickerButton : UserControl
{
    public static readonly DependencyProperty SelectedHexProperty =
        DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorPickerButton),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SelectedHex
    {
        get => (string)GetValue(SelectedHexProperty);
        set => SetValue(SelectedHexProperty, value);
    }

    public ColorPickerButton() => InitializeComponent();

    private void Swatch_Click(object sender, RoutedEventArgs e)
        => PickerPopup.IsOpen = !PickerPopup.IsOpen;
}
