using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Python.Runtime;

namespace Alife.Function.Speech;

[Plugin("Genie语音合成", "基于GPT-SoVITS的本地离线语音合成引擎",
defaultCategory: "Alife 官方/模型接入/语音模型",
EditorUI = typeof(GenieSpeechModelUI))]
public class GenieSpeechModel :
    ISpeechModel,
    IDisposable,
    IConfigurable<GenieSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "Genie");

    public GenieSpeechModelConfig? Configuration { get; set; }
    
    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // 生成缓存文件名
        string md5Hash;
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            md5Hash = Convert.ToHexString(hashBytes);
        }

        string safeFileName = $"genie_{md5Hash}.wav";
        string outputPath = Path.Combine(AlifePath.TempFolderPath, safeFileName);

        if (File.Exists(outputPath))
            return outputPath;

        // 在 Task.Run 中执行，与 VITS 行为一致
        return await Task.Run(() => {
            using (Py.GIL())
            {
                dynamic synthesize = pythonModule!.GetAttr("synthesize");
                dynamic result = synthesize(
                new PyString(text),
                new PyString(outputPath)
                );

                string status = result["status"];
                if (status == "ok")
                {
                    string resultPath = result["result"];
                    if (File.Exists(resultPath))
                        return resultPath;
                }

                string message = result["message"];
                AlifeTerminal.LogWarning($"Genie synthesis failed: {message}");
                return null;
            }
        }, cancellationToken);
    }

    readonly PyModule? pythonModule;
    readonly string pythonCode =
        """"
        import genie_tts as genie
        import traceback

        def init(model_path):
            genie.load_predefined_character('feibi')

        def synthesize(text, output_path):
            try:
                genie.tts(
                    character_name='feibi',
                    text=text,
                    save_path=output_path
                )
                return {'status': 'ok', 'result': output_path}
            except Exception as e:
                return {'status': 'error', 'message': traceback.format_exc()}
        """";

    public GenieSpeechModel()
    {
        string modelPath = RuntimeFolder;

        // 安装 genie-tts
        AlifePlatform.Command("python", "-m pip install genie-tts");

        // 初始化 Python 环境（参考 VITS 的实现方式）
        using (Py.GIL())
        {
            pythonModule = Py.CreateScope(nameof(GenieSpeechModel));
            pythonModule.Exec(pythonCode);
            pythonModule.GetAttr("init").Invoke(new PyString(modelPath));
        }
    }
    public void Dispose()
    {
        using (Py.GIL())
        {
            pythonModule?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
