using System.Windows;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
