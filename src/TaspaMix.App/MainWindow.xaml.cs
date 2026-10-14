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
    private readonly Dictionary<string, List<string>> _playlists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, AudioPlaybackService> _soundPlayers = new();
    private readonly Dictionary<int, Button> _soundSlotButtons = new();
    private string _currentAudioPath = string.Empty;
    private object? _powerPointPresentation;
    private bool _videoMuted;
    private bool _vocalReducedUi;
    private readonly Dictionary<int, string> _soundSlots = new();
    private string SoundSlotsFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "sound-slots.json");
    private string PlaylistFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "playlists.json");

    public MainWindow()
    {
        InitializeComponent();
        MediaGrid.ItemsSource = _visibleMedia;
        LoadPlaylists();
        LoadMediaLibrary();
        LoadSoundSlots();
        BuildSoundSlots();
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
            "Плейлисты" => $"Сохранённых плейлистов: {_playlists.Count}. Добавление треков сохраняется автоматически.",
            _ => $"Раздел «{section}»: функции подключаются поэтапно."
        };
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var kindFilter = _activeSection == "Видео" ? "Видео" : "Аудио";
        var dialog = new OpenFolderDialog { Title = kindFilter == "Видео" ? "Выберите папку с видео" : "Выберите папку с музыкой", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm" };
        int added = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories))
            {
                if (!supported.Contains(System.IO.Path.GetExtension(path))) continue;
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var kind = ext is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" ? "Аудио" : "Видео";
                if ((kindFilter == "Видео" && kind != "Видео") || (kindFilter == "Аудио" && kind != "Аудио")) continue;
                if (!_knownPaths.Add(path)) continue;
                _allMedia.Add(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), kind, path) { Thumbnail = kind == "Видео" ? VideoThumbnailLoader.TryGetThumbnail(path) : null }); added++;
            }
            SaveMediaLibrary(); ApplyFilter(); LibraryStatus.Text = $"Добавлено: {added} файлов ({kindFilter}); библиотека сохранена.";
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
            "Плейлисты" => _playlists.SelectMany(p => p.Value.Select(path => new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), "Аудио", path, p.Key))),
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

            _audio.Stop();
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

    private void Start_Click(object sender, RoutedEventArgs e) { _audio.Stop(); NowPlaying.Text = "Музыка остановлена — запуск программы"; Program_Click(sender, e); }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        _programWindow?.StopPlayback();
        NowPlaying.Text = "Ничего не воспроизводится";
    }

    private void Placeholder_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Выбери слот звука и назначь ему аудиофайл.", "taspaMIX");

    private void BuildSoundSlots()
    {
        SoundSlotsPanel.Items.Clear();
        _soundSlotButtons.Clear();
        for (int i = 1; i <= 30; i++)
        {
            var slot = i;
            var label = _soundSlots.TryGetValue(slot, out var path) && File.Exists(path)
                ? $"{slot:00} · {System.IO.Path.GetFileNameWithoutExtension(path)}"
                : $"{slot:00} · Назначить";
            var button = new Button { Content = label, Tag = slot, Margin = new Thickness(3), MinHeight = 42, ToolTip = "Нажми: назначить / воспроизвести / остановить" };
            button.Click += SoundSlot_Click;
            _soundSlotButtons[slot] = button;
            SoundSlotsPanel.Items.Add(button);
        }
    }

    private void SoundSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var slot)) return;
        if (_soundPlayers.TryGetValue(slot, out var playing) && playing.IsPlaying)
        {
            playing.Stop();
            playing.Dispose();
            _soundPlayers.Remove(slot);
            LibraryStatus.Text = $"Тычка {slot:00} остановлена.";
            return;
        }
        if (!_soundSlots.TryGetValue(slot, out var path) || !File.Exists(path))
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Аудио|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg", Title = "Назначить звук для слота " + slot };
            if (dialog.ShowDialog() != true) return;
            _soundSlots[slot] = path = dialog.FileName;
            SaveSoundSlots();
            button.Content = $"{slot:00} · {System.IO.Path.GetFileNameWithoutExtension(path)}";
        }
        try
        {
            if (_soundPlayers.Remove(slot, out var old)) old.Dispose();
            _audio.Stop();
            NowPlaying.Text = "Тычка играет — фоновая музыка остановлена";
            var player = new AudioPlaybackService();
            _soundPlayers[slot] = player;
            player.Play(path);
            LibraryStatus.Text = $"Тычка {slot:00} играет один раз. Нажми этот же слот ещё раз для остановки.";
        }
        catch (Exception ex) { MessageBox.Show("Не удалось воспроизвести тычку: " + ex.Message, "taspaMIX"); }
    }

    private void LoadSoundSlots()
    {
        try
        {
            if (File.Exists(SoundSlotsFile))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(SoundSlotsFile));
                if (data != null) foreach (var pair in data) _soundSlots[pair.Key] = pair.Value;
            }
        }
        catch { }
    }

    private void SaveSoundSlots()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SoundSlotsFile)!);
        File.WriteAllText(SoundSlotsFile, System.Text.Json.JsonSerializer.Serialize(_soundSlots, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem item || item.Kind != "Видео")
        {
            MessageBox.Show("Выбери видео в библиотеке, чтобы загрузить его в Preview.", "taspaMIX");
            return;
        }
        LoadPreview(item.Path);
    }

    private void Media_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Preview_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
        {
            var path = files.FirstOrDefault(IsVideoFile);
            if (path != null) LoadPreview(path);
            else LibraryStatus.Text = "Перетащи видеофайл (MP4, MKV, MOV, AVI) в окно Preview.";
        }
    }

    private void Program_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
        {
            var path = files.FirstOrDefault(IsVideoFile);
            if (path != null) PlayMedia(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), "Видео", path));
            else LibraryStatus.Text = "Перетащи видеофайл в окно Program.";
        }
    }

    private static bool IsVideoFile(string path) =>
        new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void LoadPreview(string path)
    {
        try
        {
            PreviewPlayer.Stop();
            PreviewPlayer.Source = new Uri(path, UriKind.Absolute);
            PreviewPlayer.Position = TimeSpan.Zero;
            PreviewPlayer.Play();
            LibraryStatus.Text = "Видео загружено в Preview: " + System.IO.Path.GetFileName(path);
        }
        catch (Exception ex) { MessageBox.Show("Не удалось загрузить Preview: " + ex.Message, "taspaMIX"); }
    }

    private void PreviewPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        LibraryStatus.Text = "Preview не смог открыть этот формат. Попробуй MP4 с H.264/AAC.";
    }

    private void PowerPointNext_Click(object sender, RoutedEventArgs e)
    {
        try { dynamic presentation = _powerPointPresentation!; presentation.SlideShowWindow.View.Next(); }
        catch { MessageBox.Show("Сначала запусти презентацию PowerPoint.", "taspaMIX"); }
    }

    private void PowerPointPrevious_Click(object sender, RoutedEventArgs e)
    {
        try { dynamic presentation = _powerPointPresentation!; presentation.SlideShowWindow.View.Previous(); }
        catch { MessageBox.Show("Сначала запусти презентацию PowerPoint.", "taspaMIX"); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _audio.Dispose();
        foreach (var player in _soundPlayers.Values) player.Dispose();
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
            MessageBox.Show("Выбери аудиотрек в разделе «Музыка».", "taspaMIX");
            return;
        }
        var dialog = new PlaylistNameWindow { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.PlaylistName)) return;
        var name = dialog.PlaylistName.Trim();
        if (!_playlists.TryGetValue(name, out var tracks)) _playlists[name] = tracks = new List<string>();
        if (!tracks.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) tracks.Add(item.Path);
        SavePlaylists();
        LibraryStatus.Text = $"Автосохранено: «{item.Name}» → плейлист «{name}»";
    }

    private void LoadPlaylists()
    {
        try
        {
            if (File.Exists(PlaylistFile))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(PlaylistFile));
                if (loaded != null) foreach (var pair in loaded) _playlists[pair.Key] = pair.Value;
            }
        }
        catch { }
    }

    private void SavePlaylists()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlaylistFile)!);
        File.WriteAllText(PlaylistFile, System.Text.Json.JsonSerializer.Serialize(_playlists, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (_activeSection == "Плейлисты") ApplyFilter();
    }

    private string MediaLibraryFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "media-library.json");

    private void LoadMediaLibrary()
    {
        try
        {
            if (!File.Exists(MediaLibraryFile)) return;
            var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(MediaLibraryFile)) ?? new();
            foreach (var path in paths.Where(File.Exists))
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var kind = ext is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" ? "Аудио" : "Видео";
                if (_knownPaths.Add(path)) _allMedia.Add(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), kind, path) { Thumbnail = kind == "Видео" ? VideoThumbnailLoader.TryGetThumbnail(path) : null });
            }
            ApplyFilter();
        }
        catch { }
    }

    private void SaveMediaLibrary()
    {
        var root = System.IO.Path.GetDirectoryName(MediaLibraryFile)!;
        Directory.CreateDirectory(root);
        File.WriteAllText(MediaLibraryFile, System.Text.Json.JsonSerializer.Serialize(_allMedia.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void Vocal_Click(object sender, RoutedEventArgs e)
    {
        _audio.ToggleVocalReduction();
        _vocalReducedUi = _audio.VocalReduced;
        if (sender is Button button) button.Content = _vocalReducedUi ? "VOCAL: OFF" : "VOCAL: ON";
        LibraryStatus.Text = _vocalReducedUi
            ? "Vocal reduction включён (стерео center-cancel; результат зависит от сведения трека)."
            : "Оригинальное стерео-воспроизведение восстановлено.";
    }

    private void ToggleVideoAudio_Click(object sender, RoutedEventArgs e)
    {
        _videoMuted = !_videoMuted;
        _programWindow?.SetMuted(_videoMuted);
        if (sender is Button button) button.Content = _videoMuted ? "VIDEO AUDIO: OFF" : "VIDEO AUDIO: ON";
    }

    private void PowerPoint_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PowerPoint (*.pptx;*.ppt)|*.pptx;*.ppt", Title = "Выбери презентацию PowerPoint" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application", true)!)!;
            app.Visible = true;
            dynamic presentation = app.Presentations.Open(dialog.FileName, true, false, false);
            _powerPointPresentation = presentation;
            dynamic settings = presentation.SlideShowSettings;
            var screens = System.Windows.Forms.Screen.AllScreens;
            settings.ShowType = 3;
            settings.Run();
            LibraryStatus.Text = "PowerPoint запущен через Microsoft PowerPoint: анимации сохраняются. Управляй стрелками/пробелом; закрытие слайда — Esc.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Для сохранения анимаций нужен установленный Microsoft PowerPoint. Ошибка: " + ex.Message, "taspaMIX — PowerPoint");
        }
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

public sealed record MediaItem(string Name, string Kind, string Path, string Playlist = "")
{
    public System.Windows.Media.ImageSource? Thumbnail { get; init; }
}

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

    public void SetMuted(bool muted) => _player.IsMuted = muted;

    public void StopPlayback()
    {
        _player.Stop();
        _player.Source = null;
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
    private readonly System.Windows.Controls.TextBox _nameBox;
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
        _nameBox = new System.Windows.Controls.TextBox { Text = "Мой плейлист", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(_nameBox);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(10, 6, 10, 6), IsDefault = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(PlaylistName)) DialogResult = true; };
        panel.Children.Add(save);
        Content = panel;
    }
}
