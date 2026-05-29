using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TradingDashboard.Models
{
    public class NewsItem : INotifyPropertyChanged
    {
        private string _thumbnailUrl = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PubDate { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ThumbnailText { get; set; } = "NEWS";
        public string ThumbnailUrl
        {
            get => _thumbnailUrl;
            set
            {
                if (_thumbnailUrl == value)
                    return;

                _thumbnailUrl = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
