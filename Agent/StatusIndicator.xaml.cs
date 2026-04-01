using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Agent
{
    public partial class StatusIndicator : UserControl
    {
        public enum StatusType { Gray, Green, Orange, Red }

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(StatusType), typeof(StatusIndicator),
                new PropertyMetadata(StatusType.Gray, OnStatusChanged));

        public StatusType Status
        {
            get => (StatusType)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public Brush StatusBrush
        {
            get
            {
                return Status switch
                {
                    StatusType.Green => new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0)),
                    StatusType.Orange => new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)),
                    StatusType.Red => new SolidColorBrush(Color.FromRgb(0xF1, 0x4C, 0x4C)),
                    _ => new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))
                };
            }
        }

        public StatusIndicator()
        {
            InitializeComponent();
        }

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StatusIndicator indicator)
            {
                indicator.OnPropertyChanged(new DependencyPropertyChangedEventArgs(
                    StatusBrushProperty, null, indicator.StatusBrush));
            }
        }

        public static readonly DependencyProperty StatusBrushProperty =
            DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(StatusIndicator),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))));

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == StatusProperty)
            {
                UpdateBrush();
            }
        }

        private void UpdateBrush()
        {
            SetValue(StatusBrushProperty, StatusBrush);
        }
    }
}
