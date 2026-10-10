using System.Security.Cryptography;

namespace League.Services;

public sealed class VoiceRecognitionException(string message) : InvalidOperationException(message);
public sealed record VoiceModelFile(string Name,long Size,string Sha256);

/// <summary>Pinned local model artifacts only. Audio never enters the downloader.</summary>
public sealed class VoiceModelStore
{
    public const string ModelId="SenseVoice-2024-07-17-int8";
    public const string Source="https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/2365baeacb507f821a0c8120fcee3d484dba7a07/";
    public static readonly VoiceModelFile[] Files=[
        new("model.int8.onnx",239233841,"c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51"),
        new("tokens.txt",315894,"f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc")];
    public static VoiceModelStore Shared {get;}=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"League","VoiceModels",ModelId));
    private readonly SemaphoreSlim _download=new(1,1);
    private readonly Dictionary<string,string> _checked=new();
    private readonly VoiceModelFile[] _files;
    private readonly Func<HttpClient> _client;
    public string DirectoryPath {get;}
    public VoiceModelStore(string directory,Func<HttpClient>? client=null,VoiceModelFile[]? files=null)
    {DirectoryPath=directory;_client=client??(()=>new HttpClient{Timeout=Timeout.InfiniteTimeSpan});_files=files??Files;}
    private bool HasFiles(string directory)=>_files.All(f=>File.Exists(Path.Combine(directory,f.Name)) && new FileInfo(Path.Combine(directory,f.Name)).Length==f.Size);
    public string? AvailableDirectory
    {
        get
        {
            try
            {
                if(HasFiles(DirectoryPath))return DirectoryPath;
                string bundled=Path.Combine(AppContext.BaseDirectory,"VoiceModels","SenseVoice");
                return HasFiles(bundled)?bundled:null;
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){return null;}
        }
    }
    public async Task<string> ValidateAsync(CancellationToken token)
    {
        string directory=AvailableDirectory??throw new VoiceRecognitionException("请先到设置 → 消息发送 → 本机语音识别下载模型（约 228 MiB），再开始录音。");
        foreach(var file in _files)
        {
            token.ThrowIfCancellationRequested();string path=Path.Combine(directory,file.Name);
            var info=new FileInfo(path);string signature=info.Length+":"+info.LastWriteTimeUtc.Ticks;
            lock(_checked)if(_checked.GetValueOrDefault(path)==signature)continue;
            if(!await MatchesAsync(path,file,token).ConfigureAwait(false))throw new VoiceRecognitionException("语音模型校验失败，请在设置 → 消息发送重新下载模型。");
            lock(_checked)_checked[path]=signature;
        }
        return directory;
    }
    public async Task DownloadAsync(IProgress<int>? progress,CancellationToken token)
    {
        await _download.WaitAsync(token).ConfigureAwait(false);
        var temporary=new List<string>();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var client=_client();long total=_files.Sum(f=>f.Size),received=0;int last=-1;
            foreach(var file in _files)
            {
                string destination=Path.Combine(DirectoryPath,file.Name);
                if(await MatchesAsync(destination,file,timeout.Token).ConfigureAwait(false)){received+=file.Size;Report();continue;}
                string part=destination+"."+Guid.NewGuid().ToString("N")+".download";temporary.Add(part);
                using var response=await client.GetAsync(Source+file.Name,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);response.EnsureSuccessStatusCode();
                await using(var input=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
                await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,true))
                {
                    byte[] buffer=new byte[128*1024];long bytes=0;int count;
                    while((count=await input.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0)
                    {
                        bytes+=count;if(bytes>file.Size)throw new IOException("语音模型下载内容大小不正确。");
                        await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token).ConfigureAwait(false);received+=count;Report();
                    }
                }
                if(!await MatchesAsync(part,file,timeout.Token).ConfigureAwait(false))throw new IOException("语音模型下载校验失败，请重试。");
                File.Move(part,destination,true);
            }
            // Keep the upstream model license reference with the local weights.
            await File.WriteAllTextAsync(Path.Combine(DirectoryPath,"LICENSE"),"SenseVoice model: https://github.com/FunAudioLLM/SenseVoice\nModel license: https://github.com/modelscope/FunASR#license\nExported model: "+Source+"\n",timeout.Token).ConfigureAwait(false);
            progress?.Report(100);
            void Report(){int value=(int)Math.Min(99,received*100/total);if(value!=last){last=value;progress?.Report(value);}}
        }
        finally
        {
            foreach(string path in temporary)try{File.Delete(path);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
            _download.Release();
        }
    }
    private static async Task<bool> MatchesAsync(string path,VoiceModelFile file,CancellationToken token)
    {
        if(!File.Exists(path) || new FileInfo(path).Length!=file.Size)return false;
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
