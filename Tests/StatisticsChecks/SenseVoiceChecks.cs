using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using League.Services;
using NAudio.Wave;

internal static class SenseVoiceChecks
{
    private static void Check(bool value, string note)
    {
        if (!value)
        {
            throw new Exception(note);
        }
    }

    public static async Task RunAsync()
    {
        CaptureBoundsChecks();
        CaptureLevelChecks();
        await CaptureLifecycleChecks();
        await ParallelPreparationChecksAsync();
        await CancelDuringDecodeChecksAsync();
        await FailedModelChecksAsync();
        await CancelDuringPreparationChecksAsync();
        await DownloadChecks();
        Console.WriteLine(
            "PASS: SenseVoice capture/model stages, stop before decoding, cancellation, audio erasure, silence trimming, duration cap, model download/hash rejection/repair. No microphone or game chat used."
        );
    }

    private static void CaptureBoundsChecks()
    {
        var pcm = new VoicePcmBuffer();
        pcm.Append(new byte[3200]);
        Check(pcm.Finish().Length == 0, "Pure silence is not sent to a model that could hallucinate text");
        byte[] tone = new byte[32000];
        for (int i = 0; i < tone.Length / 2; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                tone.AsSpan(i * 2, 2),
                (short)(Math.Sin(i * .15) * 10000)
            );
        }

        pcm.Append(tone);
        pcm.Append(new byte[64000]);
        var trimmed = pcm.Finish();
        Check(
            trimmed.Length < pcm.Count && trimmed.Any(x => x != 0),
            "Leading/trailing silence is trimmed while speech context remains"
        );
        pcm.Append(new byte[VoicePcmBuffer.MaxSamples * 4]);
        Check(pcm.Count == VoicePcmBuffer.MaxSamples, "Capture is bounded to 20 seconds of mono PCM");
    }

    private static async Task ParallelPreparationChecksAsync()
    {
        var updates = new ConcurrentQueue<VoiceRecognitionUpdate>();
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();
        using (var session = new SenseVoiceSession(capture, transcriber))
        {
            session.Update += updates.Enqueue;
            var run = session.RunAsync(CancellationToken.None);
            await transcriber.Started.Task;
            await capture.Started.Task;
            Check(
                !transcriber.Prepared.Task.IsCompleted && updates.Any(u => u.Stage == "recording"),
                "The microphone can record while the model is still loading"
            );
            session.Stop();
            await capture.ReleasedSignal.Task;
            Check(
                !transcriber.Decoding.Task.IsCompleted && capture.Samples.Any(x => x != 0),
                "An early Stop retains audio until the model is ready"
            );
            transcriber.Prepared.SetResult();
            await transcriber.Decoding.Task;
            Check(
                capture.Released && updates.Any(u => u.Stage == "decoding") && !updates.Any(u => u.Final),
                "Stopping releases the microphone before decoding; no provisional text is final"
            );
            transcriber.Text.SetResult("等队友到齐再开团。");
            var outcome = await run;
            Check(
                !outcome.Cancelled
                    && updates.Count(u => u.Final) == 1
                    && updates.Last(u => u.Final).Text == "等队友到齐再开团。"
                    && float.IsNaN(updates.Last(u => u.Final).Confidence),
                "Exactly one final transcript; no fabricated confidence score"
            );
            Check(
                transcriber.Disposed && capture.Samples.All(x => x == 0),
                "Audio and model resources are released after a recording"
            );
        }
    }

    private static async Task CancelDuringDecodeChecksAsync()
    {
        var updates = new ConcurrentQueue<VoiceRecognitionUpdate>();
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();
        using (var session = new SenseVoiceSession(capture, transcriber))
        {
            session.Update += updates.Enqueue;
            var run = session.RunAsync(CancellationToken.None);
            await transcriber.Started.Task;
            transcriber.Prepared.SetResult();
            await capture.Started.Task;
            session.Stop();
            await transcriber.Decoding.Task;
            session.Dispose();
            Check(
                (await run).Cancelled && !updates.Any(u => u.Final) && transcriber.Disposed,
                "Closing during decoding discards late results"
            );
        }
    }

    private static async Task FailedModelChecksAsync()
    {
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();
        using (var session = new SenseVoiceSession(capture, transcriber))
        {
            var run = session.RunAsync(CancellationToken.None);
            await transcriber.Started.Task;
            transcriber.Prepared.SetException(new VoiceRecognitionException("模型不可用"));
            bool failed = false;
            try
            {
                await run;
            }
            catch (VoiceRecognitionException)
            {
                failed = true;
            }
            Check(
                failed && capture.Released && transcriber.Disposed,
                "A failed parallel model load cancels and releases microphone capture"
            );
        }
    }

    private static async Task CancelDuringPreparationChecksAsync()
    {
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();
        using (var session = new SenseVoiceSession(capture, transcriber))
        {
            var run = session.RunAsync(CancellationToken.None);
            await capture.Started.Task;
            await transcriber.Started.Task;
            session.Dispose();
            Check(
                (await run.WaitAsync(TimeSpan.FromSeconds(3))).Cancelled
                    && capture.Released
                    && transcriber.Disposed
                    && !transcriber.Decoding.Task.IsCompleted,
                "Closing while the model loads cancels both workers without decoding"
            );
        }
    }

    private static async Task CaptureLifecycleChecks()
    {
        await CaptureReadinessChecksAsync();
        await CaptureCancellationChecksAsync();
        await MissingCaptureDataChecksAsync();
    }

    private static async Task CaptureReadinessChecksAsync()
    {
        var input = new FakeInput();
        var capture = new MicrophoneVoiceCapture(() => input);
        var updates = new ConcurrentQueue<VoiceRecognitionUpdate>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<float[]> run = capture.RecordAsync(
            u =>
            {
                updates.Enqueue(u);
                if (u.Stage == "recording")
                {
                    ready.TrySetResult();
                }
            },
            CancellationToken.None
        );
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(updates.IsEmpty, "StartRecording alone cannot prompt the user to speak before any device data arrives");
        byte[] data = new byte[32000];
        for (int i = 0; i < data.Length / 2; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                data.AsSpan(i * 2, 2),
                (short)(Math.Sin(i * .15) * 60)
            );
        }

        input.Emit(data);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        capture.Stop();
        var samples = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Check(
            samples.Length == 16000 && samples.Any(x => x != 0) && input.Stopped && input.Disposed,
            "The real capture callback path retains quiet PCM and releases its device on Stop"
        );
        Check(
            updates.Count > 0
                && input.WaveFormat.SampleRate == 16000
                && input.WaveFormat.Channels == 1
                && input.WaveFormat.BitsPerSample == 16,
            "Capture and the model agree on PCM format"
        );
        Array.Clear(samples);
    }

    private static async Task CaptureCancellationChecksAsync()
    {
        var input = new FakeInput();
        var capture = new MicrophoneVoiceCapture(() => input);
        using var cancelled = new CancellationTokenSource();
        var run = capture.RecordAsync(_ => { }, cancelled.Token);
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        input.Emit(CreateQuietCaptureData());
        await cancelled.CancelAsync();
        bool rejected = false;
        try
        {
            // Observe the capture worker's cancellation and cleanup, not a cancelled test-side wait.
            await run.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            rejected = true;
        }
        Check(
            rejected && input.Stopped && input.Disposed,
            "Cancelling after audio arrives releases the device and returns no audio for decoding"
        );
    }

    private static byte[] CreateQuietCaptureData()
    {
        byte[] data = new byte[32000];
        for (int i = 0; i < data.Length / 2; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                data.AsSpan(i * 2, 2),
                (short)(Math.Sin(i * .15) * 60)
            );
        }

        return data;
    }

    private static async Task MissingCaptureDataChecksAsync()
    {
        var input = new FakeInput();
        var capture = new MicrophoneVoiceCapture(() => input);
        var run = capture.RecordAsync(_ => { }, CancellationToken.None);
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        capture.Stop();
        bool rejected = false;
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (VoiceRecognitionException e)
        {
            rejected = e.Message.Contains("没有返回录音数据");
        }
        Check(rejected && input.Disposed, "A device with no callbacks is reported separately from silence");
    }

    private sealed class FakeInput : IWaveIn
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped,
            Disposed;
        public WaveFormat WaveFormat { get; set; } = new(16000, 16, 1);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording() => Started.TrySetResult();

        public void StopRecording()
        {
            Stopped = true;
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose() => Disposed = true;

        public void Emit(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
    }

    private static void CaptureLevelChecks()
    {
        static byte[] Tone(int amplitude, int length)
        {
            byte[] data = new byte[length * 2];
            for (int i = 0; i < length; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                    data.AsSpan(i * 2, 2),
                    (short)(Math.Sin(i * .15) * amplitude)
                );
            }

            return data;
        }
        byte[] quiet = Tone(60, 16000);
        var chunked = new VoicePcmBuffer();
        var whole = new VoicePcmBuffer();
        whole.Append(quiet);
        for (int offset = 0; offset < quiet.Length; offset += 1600)
        {
            chunked.Append(quiet.AsSpan(offset, Math.Min(1600, quiet.Length - offset)));
        }

        var audio = chunked.Finish();
        Check(
            audio.Length == 16000 && audio.Any(x => Math.Abs(x) > .01f),
            "Speech below the former .004 cutoff survives and receives bounded gain"
        );
        Check(audio.SequenceEqual(whole.Finish()), "Detection is independent of capture-packet size");
        Check(chunked.Level > 0 && chunked.Peak > 0, "Capture reports activity separately from recognition confidence");
        var veryQuiet = new VoicePcmBuffer();
        veryQuiet.Append(Tone(9, 16000));
        Check(
            veryQuiet.Finish().Length == 16000,
            "Very quiet microphone input is not rejected by the old absolute gate"
        );
        var nearZero = new VoicePcmBuffer();
        nearZero.Append(Tone(2, 16000));
        Check(nearZero.Finish().Length == 0, "Near-digital silence does not reach the speech model");
        var click = new VoicePcmBuffer();
        click.Append(Tone(10000, 320));
        click.Append(new byte[32000]);
        Check(click.Finish().Length == 0, "One brief click does not become a voice recording");
        chunked.Clear();
        Check(
            chunked.Count == 0 && chunked.Peak == 0 && chunked.Level == 0 && chunked.Finish().Length == 0,
            "Clearing also erases activity and voice bounds"
        );
        Array.Clear(audio);
    }

    private static async Task DownloadChecks()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-model-check-" + Guid.NewGuid().ToString("N"));
        byte[] good = Encoding.UTF8.GetBytes("pinned model fixture");
        bool corrupt = false;
        var files = new[]
        {
            new VoiceModelFile("fixture.bin", good.Length, Convert.ToHexString(SHA256.HashData(good))),
        };
        var store = new VoiceModelStore(
            directory,
            () =>
                new HttpClient(
                    new Handler(_ =>
                        new(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(corrupt ? new byte[good.Length] : good),
                        }
                    )
                ),
            files
        );
        try
        {
            await store.DownloadAsync(null, CancellationToken.None);
            Check(
                await store.ValidateAsync(CancellationToken.None) == directory,
                "Only verified files become available"
            );
            string target = Path.Combine(directory, "fixture.bin");
            await File.WriteAllBytesAsync(target, new byte[good.Length]);
            bool rejected = false;
            try
            {
                await store.ValidateAsync(CancellationToken.None);
            }
            catch (VoiceRecognitionException)
            {
                rejected = true;
            }
            Check(rejected, "A same-size corrupt model cannot be loaded");
            corrupt = true;
            rejected = false;
            try
            {
                await store.DownloadAsync(null, CancellationToken.None);
            }
            catch (IOException)
            {
                rejected = true;
            }
            Check(
                rejected && Directory.GetFiles(directory, "*.download").Length == 0,
                "Failed payload is not published and its partial file is removed"
            );
            corrupt = false;
            await store.DownloadAsync(null, CancellationToken.None);
            Check(
                (await File.ReadAllBytesAsync(target)).SequenceEqual(good),
                "A broken model can be repaired without touching unrelated files"
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    public static async Task EngineAsync(string root)
    {
        var store = new VoiceModelStore(root);
        using var recognizer = new SenseVoiceTranscriber(store);
        var watch = Stopwatch.StartNew();
        await recognizer.PrepareAsync(CancellationToken.None);
        double load = watch.Elapsed.TotalMilliseconds;
        var reports = new List<string>();
        await OfficialSampleChecksAsync(root, recognizer, reports);
        using var synth = new SpeechSynthesizer();
        var voice = synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN");
        Check(voice != null, "Chinese synthesis is available for microphone-free chat tests");
        synth.SelectVoice(voice!.VoiceInfo.Name);
        var phrases = new[]
        {
            "先别开团，等队友到齐后一起推，注意对面绕后。",
            "我没有闪现，大招还有二十秒，先清兵再打龙。",
            "拉克丝先控制，卡莎跟输出，赵信保护后排。",
        };
        int distance = 0;
        int length = 0;
        foreach (string phrase in phrases)
        {
            using var memory = new MemoryStream();
            synth.SetOutputToAudioStream(
                memory,
                new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono)
            );
            synth.Speak(phrase);
            synth.SetOutputToNull();
            var buffer = new VoicePcmBuffer();
            buffer.Append(memory.ToArray());
            float[] samples = buffer.Finish();
            watch.Restart();
            string text = await recognizer.TranscribeAsync(samples, CancellationToken.None);
            string expected = Normalize(phrase);
            string actual = Normalize(text);
            int errors = Distance(expected, actual);
            distance += errors;
            length += expected.Length;
            reports.Add(
                $"chat: {samples.Length / 16000.0:F2}s audio / {watch.Elapsed.TotalMilliseconds:F0}ms decode / errors={errors}/{expected.Length} / {text}"
            );
            Array.Clear(samples);
        }
        Check(distance / (double)length < .25, "Chinese chat test aggregate character error rate is below 25%");
        Console.WriteLine(
            $"PASS: SenseVoice real CPU model, 2 threads, load={load:F0}ms; test character error rate={distance / (double)length:P1}. No microphone, playback or game chat."
        );
        foreach (string report in reports)
        {
            Console.WriteLine(report);
        }

        string result = Path.Combine(AppContext.BaseDirectory, "SenseVoicePreview");
        Directory.CreateDirectory(result);
        await File.WriteAllLinesAsync(
            Path.Combine(result, "recognition-check.txt"),
            new[] { $"load={load:F0}ms; test-CER={distance / (double)length:P1}" }.Concat(reports)
        );
    }

    private static async Task OfficialSampleChecksAsync(
        string root,
        SenseVoiceTranscriber recognizer,
        List<string> reports
    )
    {
        string official = Path.GetFullPath(Path.Combine(root, "../../voice-research/zh.wav"));
        if (File.Exists(official))
        {
            byte[] wav = await File.ReadAllBytesAsync(official);
            float[] samples = ReadWave(wav);
            var watch = Stopwatch.StartNew();
            string text = await recognizer.TranscribeAsync(samples, CancellationToken.None);
            Console.WriteLine("Upstream Chinese fixture: " + text);
            Check(
                text.Count(c => c is >= '\u4e00' and <= '\u9fff') >= 5,
                "The real model transcribes the upstream Chinese sample"
            );
            reports.Add(
                $"upstream: {samples.Length / 16000.0:F2}s audio / {watch.Elapsed.TotalMilliseconds:F0}ms decode / {text}"
            );
            Array.Clear(samples);
        }
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());

    private static int Distance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int i = 1; i <= left.Length; i++)
        {
            var row = new int[right.Length + 1];
            row[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                row[j] = Math.Min(
                    Math.Min(row[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1)
                );
            }

            previous = row;
        }
        return previous[^1];
    }

    private static float[] ReadWave(byte[] wav)
    {
        using var reader = new BinaryReader(new MemoryStream(wav));
        Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "Wave fixture has RIFF header");
        reader.ReadInt32();
        reader.ReadBytes(4);
        while (reader.BaseStream.Position + 8 <= wav.Length)
        {
            string kind = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int size = reader.ReadInt32();
            long next = reader.BaseStream.Position + size + (size % 2);
            if (kind == "fmt ")
            {
                Check(
                    reader.ReadInt16() == 1 && reader.ReadInt16() == 1 && reader.ReadInt32() == 16000,
                    "Wave fixture uses mono 16k PCM"
                );
                reader.ReadInt32();
                reader.ReadInt16();
                Check(reader.ReadInt16() == 16, "Wave fixture uses PCM16");
            }
            if (kind == "data")
            {
                var buffer = new VoicePcmBuffer();
                buffer.Append(reader.ReadBytes(size));
                return buffer.Finish();
            }
            reader.BaseStream.Position = next;
        }
        throw new Exception("Wave data missing");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(response(request));
    }

    private sealed class FakeCapture : IVoiceAudioCapture
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleasedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly float[] Samples = Enumerable.Repeat(.1f, 16000).ToArray();
        public bool Released;

        public async Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update, CancellationToken token)
        {
            update(new("", false));
            Started.TrySetResult();
            try
            {
                await Done.Task.WaitAsync(token);
                return Samples;
            }
            finally
            {
                Released = true;
                ReleasedSignal.TrySetResult();
            }
        }

        public void Stop() => Done.TrySetResult();

        public void Dispose()
        {
            Released = true;
            Done.TrySetResult();
        }
    }

    private sealed class FakeTranscriber : IVoiceTranscriber
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Prepared = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Decoding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<string> Text = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;

        public async Task PrepareAsync(CancellationToken token)
        {
            Started.TrySetResult();
            await Prepared.Task.WaitAsync(token);
        }

        public async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
        {
            Decoding.TrySetResult();
            return await Text.Task.WaitAsync(token);
        }

        public void Dispose() => Disposed = true;
    }
}
