using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Python.Runtime;

namespace Alife.Function.Speech;

[Plugin("VITS语音合成", "基于VITS的本地离线语音合成引擎",
defaultCategory: "Alife 官方/模型接入/语音模型",
EditorUI = typeof(VitsSpeechModelUI))]
public class VitsSpeechModel :
    ISpeechModel,
    IDisposable,
    IConfigurable<VitsSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "VITS");

    public VitsSpeechModelConfig? Configuration { get; set; }

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string md5Hash;
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            md5Hash = Convert.ToHexString(hashBytes);
        }

        string safeFileName = $"vits_{Configuration!.SpeakerId}_{md5Hash}.wav";
        string outputPath = Path.Combine(AlifePath.TempFolderPath, safeFileName);

        if (File.Exists(outputPath))
            return outputPath;

        return await Task.Run(() => {
            using (Py.GIL())
            {
                dynamic synthesize = pythonModule.GetAttr("synthesize");
                dynamic result = synthesize(
                new PyString(text),
                new PyString(outputPath),
                new PyInt(Configuration!.SpeakerId),
                new PyFloat(Configuration!.NoiseScale),
                new PyFloat(Configuration!.NoiseScaleW),
                new PyFloat(Configuration!.LengthScale)
                );

                string status = result["status"];
                if (status == "ok")
                {
                    string resultPath = result["result"];
                    if (File.Exists(resultPath))
                        return resultPath;
                }

                string message = result["message"];
                AlifeTerminal.LogWarning($"VITS synthesis failed: {message}");
                return null;
            }
        }, cancellationToken);
    }

    readonly PyModule pythonModule;
    readonly string pythonCode =
        """"
        # coding=utf-8
        import sys, os, json, traceback
        import numpy as np
        import torch
        from torch import no_grad, LongTensor

        from models import SynthesizerTrn
        from text import text_to_sequence
        import commons
        import utils

        # ---------------------------------------------------------------------------
        # Globals – populated in init()
        # ---------------------------------------------------------------------------
        device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        hps_ms = None
        net_g_ms = None


        def get_text(text, hps):
            text_norm, clean_text = text_to_sequence(text, hps.symbols, hps.data.text_cleaners)
            if hps.data.add_blank:
                text_norm = commons.intersperse(text_norm, 0)
            text_norm = LongTensor(text_norm)
            return text_norm, clean_text


        def vits(text, language, speaker_id, noise_scale, noise_scale_w, length_scale):
            text = text.replace('\n', ' ').replace('\r', '').replace(' ', '')
            if language == 0:
                text = f'[ZH]{text}[ZH]'
            elif language == 1:
                text = f'[JA]{text}[JA]'
            stn_tst, _ = get_text(text, hps_ms)
            with no_grad():
                x_tst = stn_tst.unsqueeze(0).to(device)
                x_tst_lengths = LongTensor([stn_tst.size(0)]).to(device)
                sid = LongTensor([speaker_id]).to(device)
                audio = net_g_ms.infer(
                    x_tst, x_tst_lengths, sid=sid,
                    noise_scale=noise_scale,
                    noise_scale_w=noise_scale_w,
                    length_scale=length_scale
                )[0][0, 0].data.cpu().float().numpy()
            return 22050, audio


        def init(vitsDir):
            """加载 VITS 模型（全局只需调用一次）"""
            global hps_ms, net_g_ms

            hps_ms = utils.get_hparams_from_file(f'{vitsDir}/model/config.json')
            net_g_ms = SynthesizerTrn(
                len(hps_ms.symbols),
                hps_ms.data.filter_length // 2 + 1,
                hps_ms.train.segment_size // hps_ms.data.hop_length,
                n_speakers=hps_ms.data.n_speakers,
                **hps_ms.model)
            _ = net_g_ms.eval().to(device)
            utils.load_checkpoint(f'{vitsDir}/model/G_953000.pth', net_g_ms, None)


        def synthesize(text, output_path, speaker_id=0, noise_scale=0.6, noise_scale_w=0.668, length_scale=1.2):
            """合成语音到指定路径，返回 {"status": "ok", "result": path} 或 {"status": "error", "message": ...}"""
            try:
                sr, audio = vits(text, 0, speaker_id, noise_scale, noise_scale_w, length_scale)
                audio_int16 = (audio * 32767).astype(np.int16)
                import wave
                with wave.open(output_path, 'wb') as wf:
                    wf.setnchannels(1)
                    wf.setsampwidth(2)
                    wf.setframerate(sr)
                    wf.writeframes(audio_int16.tobytes())
                return {'status': 'ok', 'result': output_path}
            except Exception:
                return {'status': 'error', 'message': traceback.format_exc()}

        """";

    public VitsSpeechModel()
    {
        //安装依赖
        string vitsDir = RuntimeFolder;
        AlifePlatform.Command("python", $"-m pip install -r {Path.Combine(vitsDir, "requirements.txt").Replace(Path.DirectorySeparatorChar, '/')}");

        //加载功能
        using (Py.GIL())
        {
            //设置环境变量
            PythonEngine.Exec(
            $"""
             import sys
             p = r'{vitsDir}'
             if p not in sys.path:
                 sys.path.insert(0, p)
             """);

            pythonModule = Py.CreateScope(nameof(VitsSpeechModel));
            pythonModule.Exec(pythonCode);
            pythonModule.GetAttr("init").Invoke(new PyString(vitsDir));
        }
    }
    public void Dispose()
    {
        using (Py.GIL())
        {
            pythonModule.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
