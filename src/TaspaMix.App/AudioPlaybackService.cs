using NAudio.Wave;

namespace TaspaMix;

public sealed class AudioPlaybackService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public void Play(string filePath)
    {
        Stop();
        var reader = new AudioFileReader(filePath);
        var output = new WaveOutEvent();
        try
        {
            output.Init(reader);
            _reader = reader;
            _output = output;
            output.Play();
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing)
            _output.Pause();
        else if (_output?.PlaybackState == PlaybackState.Paused)
            _output.Play();
    }

    public void Stop()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        if (output is not null)
        {
            output.Stop();
            output.Dispose();
        }
        reader?.Dispose();
    }

    public void Dispose() => Stop();
}
