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
    private ProgramOutputWindow? _programWindow;
    private string _activeSection = "Музыка";

    public MainWindow()
    {
        InitializeComponent();
        MediaGrid.ItemsSource = _visibleMedia;
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section }) return;
        _activeSection = section;
        SectionTitle.Text = section;
        SearchBox.Text = string.Empty;
        ApplyFilter();
        LibraryStatus.Text = section switch
        {
            "Музыка" => $"Аудиофайлов: {_allMedia.Count(x => x.Kind == "Аудио")}",
            "Видео" => $"Видеофайлов: {_allMedia.Count(x => x.Kind == "Видео")}",
            "Плейлисты" => "Плейлисты будут отображаться отдельно от общей медиатеки.",
            _ => $"Раздел «{section}»: функции подключаются поэтапно."
        };
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
        var sectionItems = _activeSection switch
        {
            "Музыка" => _allMedia.Where(x => x.Kind == "Аудио"),
            "Видео" => _allMedia.Where(x => x.Kind == "Видео"),
            "Тычки" => Enumerable.Empty<MediaItem>(),
            "Конкурсы" => Enumerable.Empty<MediaItem>(),
            "Плейлисты" => Enumerable.Empty<MediaItem>(),
            _ => _allMedia.AsEnumerable()
        };
        foreach (var item in sectionItems.Where(x => x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            _visibleMedia.Add(item);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem item) { MessageBox.Show("Сначала выберите файл в списке.", "taspaMIX"); return; }
        PlayMedia(item);
    }

    private void MediaGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (MediaGrid.SelectedItem is MediaItem item) PlayMedia(item);
    }

    private void PlayMedia(MediaItem item)
    {
        try
        {
            if (item.Kind == "Аудио")
            {
                _audio.Play(item.Path);
                NowPlaying.Text = $"♫ {item.Name}";
                return;
            }

            _selectedVideo = item;
            if (_programWindow == null || !_programWindow.IsLoaded)
                _programWindow = new ProgramOutputWindow();
            _programWindow.ShowOnOutputScreen();
            _programWindow.PlayFile(item.Path);
            NowPlaying.Text = $"▣ Видео: {item.Name}";
            LibraryStatus.Text = $"Видео запущено: {item.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть медиафайл:\n{ex.Message}", "taspaMIX");
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _audio.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        _programWindow?.StopPlayback();
        NowPlaying.Text = "Ничего не воспроизводится";
    }

    private void Placeholder_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Звуковые слоты и остальные функции будут реализованы следующим этапом.", "taspaMIX");

    protected override void OnClosed(EventArgs e)
    {
        _audio.Dispose();
        _programWindow?.Close();
        base.OnClosed(e);
    }


    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
    }

    private void Program_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem item)
        {
            MessageBox.Show("Выбери видео в списке, затем нажми «НА ЭКРАН».", "taspaMIX");
            return;
        }
        if (item.Kind != "Видео")
        {
            MessageBox.Show("Для вывода на экран выбери видеофайл.", "taspaMIX");
            return;
        }
        PlayMedia(item);
    }

    private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem item || item.Kind != "Аудио")
        {
            MessageBox.Show("Сначала выбери аудиотрек в разделе «Музыка».", "taspaMIX");
            return;
        }
        var dialog = new PlaylistNameWindow();
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.PlaylistName)) return;
        try
        {
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX");
            Directory.CreateDirectory(root);
            var file = System.IO.Path.Combine(root, "playlists.json");
            var data = File.Exists(file)
                ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(file)) ?? new()
                : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!data.TryGetValue(dialog.PlaylistName.Trim(), out var tracks))
                data[dialog.PlaylistName.Trim()] = tracks = new List<string>();
            if (!tracks.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) tracks.Add(item.Path);
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            LibraryStatus.Text = $"«{item.Name}» добавлен в плейлист «{dialog.PlaylistName.Trim()}»";
        }
        catch (Exception ex) { MessageBox.Show($"Не удалось сохранить плейлист: {ex.Message}", "taspaMIX"); }
    }

    private void LogoOverlay_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Оригинальный логотип taspaMIX встроен в шапку. Вывод логотипа поверх Program-видео будет подключён отдельно.", "taspaMIX");
    }

    private void Scene_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string scene)
            MessageBox.Show($"Выбрана сцена «{scene}». Загрузка медиа-содержимого сцены будет подключена следующим этапом.", "taspaMIX");
    }

    private void DetectDisplays_Click(object sender, RoutedEventArgs e)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        DisplayStatus.Text = $"ЭКРАНОВ: {screens.Length}";
        MessageBox.Show(string.Join(Environment.NewLine, screens.Select((s, i) => $"{i + 1}. {s.DeviceName} — {(s.Primary ? "основной" : "дополнительный")} — {s.Bounds.Width}×{s.Bounds.Height}")), "Дисплеи Windows");
    }
}

public sealed record MediaItem(string Name, string Kind, string Path);

public sealed class ProgramOutputWindow : Window
{
    private readonly MediaElement _player;

    public ProgramOutputWindow()
    {
        Title = "taspaMIX — Program Output";
        Background = System.Windows.Media.Brushes.Black;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        _player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Stop,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Volume = 1
        };
        Content = _player;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Hide(); };
    }

    public void ShowOnOutputScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var target = screens.FirstOrDefault(s => !s.Primary) ?? screens.FirstOrDefault();
        if (target != null)
        {
            Left = target.Bounds.Left;
            Top = target.Bounds.Top;
            Width = target.Bounds.Width;
            Height = target.Bounds.Height;
        }
        Show();
        WindowState = WindowState.Maximized;
        Activate();
    }

    public void PlayFile(string path)
    {
        _player.Stop();
        _player.Source = new Uri(path, UriKind.Absolute);
        _player.Position = TimeSpan.Zero;
        _player.Play();
    }

    protected override void OnClosed(EventArgs e)
    {
        _player.Stop();
        _player.Source = null;
        base.OnClosed(e);
    }
}

public sealed class PlaylistNameWindow : Window
{
    private readonly TextBox _nameBox;
    public string PlaylistName => _nameBox.Text;

    public PlaylistNameWindow()
    {
        Title = "Добавить в плейлист";
        Width = 380;
        Height = 165;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.DarkSlateGray;
        Foreground = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Название плейлиста", Margin = new Thickness(0, 0, 0, 8) });
        _nameBox = new TextBox { Text = "Мой плейлист", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(_nameBox);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(10, 6, 10, 6), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(PlaylistName)) DialogResult = true; };
        panel.Children.Add(save);
        Content = panel;
    }
}
