namespace League.Services;

public sealed class VoicePcmBuffer
{
    public const int SampleRate = 16000;
    public const int MaxSamples = SampleRate * 20;
    private readonly List<float> _samples = new(MaxSamples);
    private int _firstVoice = -1;
    private int _lastVoice;
    private double _energy;
    private int _windowSamples;
    private int _audibleSamples;
    private float _peak;
    private float _windowPeak;
    public int Count => _samples.Count;
    public float Peak => _peak;
    public int Level { get; private set; }

    public void Append(ReadOnlySpan<byte> pcm)
    {
        int count = Math.Min(pcm.Length / 2, MaxSamples - _samples.Count);
        for (int i = 0; i < count; i++)
        {
            float value = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)) / 32768f;
            _samples.Add(value);
            _energy += value * value;
            _windowSamples++;
            float absolute = Math.Abs(value);
            _peak = Math.Max(_peak, absolute);
            _windowPeak = Math.Max(_windowPeak, absolute);
            if (_windowSamples == SampleRate / 100)
            {
                AnalyzeWindow();
            }
        }
    }

    private void AnalyzeWindow()
    {
        if (_windowSamples == 0)
        {
            return;
        }

        double rms = Math.Sqrt(_energy / _windowSamples);
        // Exclude near-digital silence, not quiet speech. The old .004 threshold discarded entire recordings.
        if (rms >= .0001 && _windowPeak >= .0002)
        {
            if (_firstVoice < 0)
            {
                _firstVoice = _samples.Count - _windowSamples;
            }

            _lastVoice = _samples.Count;
            _audibleSamples += _windowSamples;
        }
        // A logarithmic activity indicator; this is not a speech-confidence score.
        Level = rms <= .00001 ? 0 : Math.Clamp((int)Math.Round((20 * Math.Log10(rms) + 80) / .8), 0, 100);
        _energy = 0;
        _windowSamples = 0;
        _windowPeak = 0;
    }

    public float[] Finish()
    {
        AnalyzeWindow();
        if (_firstVoice < 0 || _audibleSamples < SampleRate / 10)
        {
            return [];
        }

        int first = Math.Max(0, _firstVoice - SampleRate / 4);
        int last = Math.Min(_samples.Count, _lastVoice + SampleRate / 3);
        var result = _samples.GetRange(first, last - first).ToArray();
        // Bounded gain makes quiet microphones usable without changing the system's input volume.
        float gain = _peak > 0 ? Math.Min(16, .2f / _peak) : 1;
        if (gain > 1)
        {
            for (int i = 0; i < result.Length; i++)
            {
                result[i] *= gain;
            }
        }

        return result;
    }

    public void Clear()
    {
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_samples).Clear();
        _samples.Clear();
        _firstVoice = -1;
        _lastVoice = 0;
        _windowSamples = 0;
        _audibleSamples = 0;
        Level = 0;
        _energy = 0;
        _peak = 0;
        _windowPeak = 0;
    }
}
