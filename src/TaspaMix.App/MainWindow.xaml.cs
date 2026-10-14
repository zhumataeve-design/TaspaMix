using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace TaspaMix;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MediaItem> _allMedia = new();
    private readonly ObservableCollection<MediaItem> _visibleMedia = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly AudioPlaybackService _audio = new();

    public MainWindow()
    {
        InitializeComponent();
        MediaGrid.ItemsSource = _visibleMedia;
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section })
        {
            SectionTitle.Text = section;
            LibraryStatus.Text = section == "Музыка" ? $"Файлов в текущем сеансе: {_allMedia.Count}" : $"Раздел «{section}»: интерфейс подготовлен, функции будут подключаться поэтапно.";
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с аудио и видео", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm" };
        int added = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories))
            {
                if (!supported.Contains(System.IO.Path.GetExtension(path)) || !_knownPaths.Add(path)) continue;
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var kind = ext is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" ? "Аудио" : "Видео";
                _allMedia.Add(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), kind, path)); added++;
            }
            ApplyFilter(); LibraryStatus.Text = $"Добавлено: {added}; всего в сеансе: {_allMedia.Count}";
        }
        catch (UnauthorizedAccessException) { MessageBox.Show("Нет доступа к одной из папок.", "taspaMIX"); }
        catch (IOException ex) { MessageBox.Show($"Ошибка чтения: {ex.Message}", "taspaMIX"); }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (MediaGrid is null) return;
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        _visibleMedia.Clear();
        foreach (var item in _allMedia.Where(x => x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            _visibleMedia.Add(item);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem item) { MessageBox.Show("Сначала выберите файл в списке.", "taspaMIX"); return; }
        if (item.Kind != "Аудио") { MessageBox.Show("Пока доступно воспроизведение аудиофайлов. Видеодвижок будет добавлен отдельно.", "taspaMIX"); return; }
        try
        {
            _audio.Play(item.Path);
            NowPlaying.Text = $"♫ {item.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось воспроизвести файл:\n{ex.Message}", "taspaMIX");
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _audio.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        NowPlaying.Text = "Ничего не воспроизводится";
    }

    private void Placeholder_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Звуковые слоты и остальные функции будут реализованы следующим этапом.", "taspaMIX");

    protected override void OnClosed(EventArgs e)
    {
        _audio.Dispose();
        base.OnClosed(e);
    }

    private void DetectDisplays_Click(object sender, RoutedEventArgs e)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        DisplayStatus.Text = $"ЭКРАНОВ: {screens.Length}";
        MessageBox.Show(string.Join(Environment.NewLine, screens.Select((s, i) => $"{i + 1}. {s.DeviceName} — {(s.Primary ? "основной" : "дополнительный")} — {s.Bounds.Width}×{s.Bounds.Height}")), "Дисплеи Windows");
    }
}

public sealed record MediaItem(string Name, string Kind, string Path);
