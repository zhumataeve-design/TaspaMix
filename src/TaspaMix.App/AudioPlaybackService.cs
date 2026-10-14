using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TaspaMix;

public sealed class AudioPlaybackService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private string? _currentPath;
    private bool _vocalReduced;
    private LiveVocalCancelSampleProvider? _liveVocalProvider;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool VocalReduced => _vocalReduced;
    public TimeSpan CurrentTime => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan TotalTime => _reader?.TotalTime ?? TimeSpan.Zero;
    public void Seek(TimeSpan position) { if (_reader is not null) _reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : (position > _reader.TotalTime ? _reader.TotalTime : position); }

    public void Play(string filePath) { _currentPath = filePath; StartPlayback(filePath); }

    private void StartPlayback(string filePath)
    {
        StopOutput();
        var reader = new AudioFileReader(filePath);
        var output = new WaveOutEvent();
        try
        {
            ISampleProvider source = reader;
            _liveVocalProvider = null;
            if (reader.WaveFormat.Channels == 2)
            {
                _liveVocalProvider = new LiveVocalCancelSampleProvider(source, _vocalReduced);
                source = _liveVocalProvider;
            }
            output.Init(new SampleToWaveProvider(source));
            _reader = reader;
            _output = output;
            output.Play();
        }
        catch { output.Dispose(); reader.Dispose(); throw; }
    }

    public void ToggleVocalReduction()
    {
        _vocalReduced = !_vocalReduced;
        if (_liveVocalProvider is not null) _liveVocalProvider.Enabled = _vocalReduced;
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing) _output.Pause();
        else if (_output?.PlaybackState == PlaybackState.Paused) _output.Play();
    }

    public void Stop() { StopOutput(); _currentPath = null; }
    private void StopOutput()
    {
        var output = _output; var reader = _reader; _output = null; _reader = null; _liveVocalProvider = null;
        if (output is not null) { output.Stop(); output.Dispose(); }
        reader?.Dispose();
    }
    public void Dispose() => Stop();

    private sealed class LiveVocalCancelSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public bool Enabled { get; set; }
        public LiveVocalCancelSampleProvider(ISampleProvider source, bool enabled) { _source = source; Enabled = enabled; }
        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (!Enabled) return read;
            for (int i = 0; i + 1 < read; i += 2)
            {
                var left = buffer[offset + i]; var right = buffer[offset + i + 1];
                buffer[offset + i] = left - right; buffer[offset + i + 1] = right - left;
            }
            return read;
        }
    }
}
