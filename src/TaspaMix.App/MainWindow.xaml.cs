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
    private string? _previewVideoPath;
    private bool _previewAudioEnabled;
    private bool _powerPointPaused;
    private string ContestFilesFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "contest-files.json");
    private readonly List<string> _contestFiles = new();
    private bool _videoMuted;
    private bool _vocalReducedUi;
    private readonly System.Windows.Threading.DispatcherTimer _timelineTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _updatingTimeline;
    private readonly Dictionary<int, string> _soundSlots = new();
    private string SoundSlotsFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "sound-slots.json");
    private string PlaylistFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "taspaMIX", "playlists.json");

    public MainWindow()
    {
        InitializeComponent();
        MediaGrid.ItemsSource = _visibleMedia;
        LoadPlaylists();
        LoadContestFiles();
        LoadMediaLibrary();
        LoadSoundSlots();
        BuildSoundSlots();
        _timelineTimer.Tick += TimelineTimer_Tick;
        _timelineTimer.Start();
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
        if (_activeSection == "Видео") { AddMediaFiles("Видео", "Видео (*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm)|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm"); return; }
        if (_activeSection == "Конкурсы") { AddContestFile(); return; }
        AddMediaFiles("Аудио", "Музыка (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg)|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg");
    }

    private void AddMediaFiles(string kind, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter + "|Все файлы (*.*)|*.*", Title = kind == "Видео" ? "Выбрать видеофайлы" : "Выбрать аудиофайлы", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        int added = 0;
        foreach (var path in dialog.FileNames)
        {
            if (!_knownPaths.Add(path)) continue;
            _allMedia.Add(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), kind, path) { Thumbnail = kind == "Видео" ? VideoThumbnailLoader.TryGetThumbnail(path) : null });
            added++;
        }
        SaveMediaLibrary(); ApplyFilter();
        LibraryStatus.Text = "Добавлено файлов: " + added;
    }

    private void AddContestFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Презентации и файлы конкурса (*.pptx;*.ppt;*.pdf;*.docx;*.xlsx)|*.pptx;*.ppt;*.pdf;*.docx;*.xlsx|Все файлы (*.*)|*.*", Title = "Выбрать файл конкурса или презентацию", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) if (!_contestFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _contestFiles.Add(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ContestFilesFile)!);
        File.WriteAllText(ContestFilesFile, System.Text.Json.JsonSerializer.Serialize(_contestFiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        ApplyFilter();
        LibraryStatus.Text = "Файлов добавлено в «Конкурсы»: " + dialog.FileNames.Length;
    }

    private void LoadContestFiles()
    {
        try { if (File.Exists(ContestFilesFile)) _contestFiles.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ContestFilesFile)) ?? new()); } catch { }
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
            "Конкурсы" => _contestFiles.Select(path => new MediaItem(System.IO.Path.GetFileNameWithoutExtension(path), "Конкурс", path)),
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

            LoadPreview(item.Path);
            NowPlaying.Text = "Видео загружено в Preview: " + item.Name;
            LibraryStatus.Text = "Предпросмотр без звука. Нажми «НА ЭКРАН», чтобы вывести на второй монитор.";
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
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
        _previewVideoPath = null;
        PlaybackTimeline.Value = 0;
        CurrentTimeText.Text = "00:00";
        RemainingTimeText.Text = "Осталось 00:00";
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
            PreviewPlayer.IsMuted = !_previewAudioEnabled;
            PreviewPlayer.Play();
            _previewVideoPath = path;
            PreviewAudioButton.Content = _previewAudioEnabled ? "Звук Preview: ON" : "Звук Preview: OFF";
            LibraryStatus.Text = "Видео в Preview без звука. Нажми «НА ЭКРАН» для вывода на экран 2.";
        }
        catch (Exception ex) { MessageBox.Show("Не удалось загрузить Preview: " + ex.Message, "taspaMIX"); }
    }

    private void PreviewAudio_Click(object sender, RoutedEventArgs e)
    {
        _previewAudioEnabled = !_previewAudioEnabled;
        PreviewPlayer.IsMuted = !_previewAudioEnabled;
        PreviewAudioButton.Content = _previewAudioEnabled ? "Звук Preview: ON" : "Звук Preview: OFF";
    }

    private void ProgramPause_Click(object sender, RoutedEventArgs e)
    {
        if (_powerPointPresentation != null) { PowerPointPause_Click(sender, e); return; }
        _programWindow?.TogglePause();
    }

    private void PowerPointPause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic p = _powerPointPresentation!;
            if (!_powerPointPaused) p.SlideShowWindow.View.Pause(); else p.SlideShowWindow.View.Resume();
            _powerPointPaused = !_powerPointPaused;
            ProgramPauseButton.Content = _powerPointPaused ? "▶ Продолжить" : "Ⅱ Пауза";
        }
        catch { MessageBox.Show("Сначала запусти презентацию PowerPoint.", "taspaMIX"); }
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e) { UpdateVideoTimeline(); }
    private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e) { try { PreviewPlayer.Stop(); PreviewPlayer.Position = TimeSpan.Zero; } catch { } PlaybackTimeline.Value = 0; }
    private void TimelineTimer_Tick(object? sender, EventArgs e)
    {
        if (_audio.TotalTime > TimeSpan.Zero && (_audio.IsPlaying || _audio.CurrentTime > TimeSpan.Zero))
        {
            var total = _audio.TotalTime.TotalSeconds; var current = _audio.CurrentTime.TotalSeconds;
            _updatingTimeline = true; PlaybackTimeline.Value = total > 0 ? current / total * 1000 : 0; _updatingTimeline = false;
            CurrentTimeText.Text = FormatTime(_audio.CurrentTime); RemainingTimeText.Text = "Осталось " + FormatTime(TimeSpan.FromSeconds(Math.Max(0, total-current))); return;
        }
        UpdateVideoTimeline();
    }
    private void UpdateVideoTimeline()
    {
        try { if (PreviewPlayer.Source == null || !PreviewPlayer.NaturalDuration.HasTimeSpan) return; var total=PreviewPlayer.NaturalDuration.TimeSpan; var current=PreviewPlayer.Position; _updatingTimeline=true; PlaybackTimeline.Value=total.TotalSeconds>0 ? current.TotalSeconds/total.TotalSeconds*1000 : 0; _updatingTimeline=false; CurrentTimeText.Text=FormatTime(current); RemainingTimeText.Text="Осталось "+FormatTime(TimeSpan.FromSeconds(Math.Max(0,total.TotalSeconds-current.TotalSeconds))); } catch { }
    }
    private void PlaybackTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingTimeline || PlaybackTimeline is null) return;
        try { if (_audio.TotalTime>TimeSpan.Zero && (_audio.IsPlaying || _audio.CurrentTime>TimeSpan.Zero)) _audio.Seek(TimeSpan.FromSeconds(_audio.TotalTime.TotalSeconds*PlaybackTimeline.Value/1000)); else if(PreviewPlayer.Source!=null && PreviewPlayer.NaturalDuration.HasTimeSpan) PreviewPlayer.Position=TimeSpan.FromSeconds(PreviewPlayer.NaturalDuration.TimeSpan.TotalSeconds*PlaybackTimeline.Value/1000); } catch { }
    }
    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    private void PreviewPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        LibraryStatus.Text = "Preview не смог открыть этот формат. Попробуй MP4 с H.264/AAC.";
    }

    private void PowerPointNext_Click(object sender, RoutedEventArgs e)
    {
        try { dynamic presentation = _powerPointPresentation!; presentation.SlideShowWindow.View.Next(); ProgramStatus.Text = "POWERPOINT • слайд " + presentation.SlideShowWindow.View.CurrentShowPosition; }
        catch { MessageBox.Show("Сначала запусти презентацию PowerPoint.", "taspaMIX"); }
    }

    private void PowerPointPrevious_Click(object sender, RoutedEventArgs e)
    {
        try { dynamic presentation = _powerPointPresentation!; presentation.SlideShowWindow.View.Previous(); ProgramStatus.Text = "POWERPOINT • слайд " + presentation.SlideShowWindow.View.CurrentShowPosition; }
        catch { MessageBox.Show("Сначала запусти презентацию PowerPoint.", "taspaMIX"); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timelineTimer.Stop();
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
        var path = _previewVideoPath;
        if (string.IsNullOrWhiteSpace(path) && MediaGrid.SelectedItem is MediaItem selected && selected.Kind == "Видео") path = selected.Path;
        if (string.IsNullOrWhiteSpace(path)) { MessageBox.Show("Сначала выбери видео и загрузи его в Preview.", "taspaMIX"); return; }
        _audio.Stop();
        if (_programWindow == null || !_programWindow.IsLoaded) _programWindow = new ProgramOutputWindow();
        _programWindow.ShowOnOutputScreen();
        _programWindow.PlayFile(path);
        ProgramStatus.Text = "VIDEO • " + System.IO.Path.GetFileName(path);
        LibraryStatus.Text = "Видео отправлено на экран 2.";
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
        string? presentationPath = null;
        if (MediaGrid.SelectedItem is MediaItem item && item.Kind == "Конкурс" &&
            System.IO.Path.GetExtension(item.Path).ToLowerInvariant() is ".ppt" or ".pptx")
            presentationPath = item.Path;
        if (presentationPath == null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PowerPoint (*.pptx;*.ppt)|*.pptx;*.ppt|Все файлы (*.*)|*.*",
                Title = "Выбери презентацию PowerPoint"
            };
            if (dialog.ShowDialog() != true) return;
            presentationPath = dialog.FileName;
        }
        try
        {
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application", true)!)!;
            app.Visible = true;
            dynamic presentation = app.Presentations.Open(presentationPath, true, false, false);
            _powerPointPresentation = presentation;
            _powerPointPaused = false;
            dynamic settings = presentation.SlideShowSettings;
            settings.ShowType = 1;
            settings.Run();
            try
            {
                dynamic showWindow = presentation.SlideShowWindow;
                var outputScreen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary);
                if (outputScreen != null)
                {
                    showWindow.Left = outputScreen.Bounds.Left;
                    showWindow.Top = outputScreen.Bounds.Top;
                    showWindow.Width = outputScreen.Bounds.Width;
                    showWindow.Height = outputScreen.Bounds.Height;
                }
            }
            catch { }
            ProgramStatus.Text = "POWERPOINT • " + System.IO.Path.GetFileName(presentationPath);
            LibraryStatus.Text = "PowerPoint запущен. Используй кнопки назад, пауза и вперёд в панели Program.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Для запуска презентации требуется установленный Microsoft PowerPoint. Подробности: " + ex.Message, "taspaMIX");
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
    private bool _paused;

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

    public void TogglePause()
    {
        try
        {
            if (_paused) _player.Play(); else _player.Pause();
            _paused = !_paused;
        }
        catch { }
    }

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
