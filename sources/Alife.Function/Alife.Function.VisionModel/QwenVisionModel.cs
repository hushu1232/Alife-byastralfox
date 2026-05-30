using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Python.Runtime;

namespace Alife.Function.Vision;

/// <summary>
/// 使用 Qwen2.5-VL-3B-Instruct 进行图像理解。
/// 通过 Python.NET 在进程内直接调用，无子进程开销。
/// </summary>
[Plugin("Qwen视觉分析", "基于Qwen2.5-VL的本地视觉分析引擎",
defaultCategory: "Alife 官方/模型接入/视觉模型",
EditorUI = typeof(QwenVisionModelUI))]
public class QwenVisionModel : IVisionModel,
    IDisposable,
    IConfigurable<QwenVisionModelConfig>
{
    public QwenVisionModelConfig? Configuration
    {
        get => new();
        set {}
    }

    public async Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() => {
                using (Py.GIL())
                {
                    dynamic queryFunc = pythonModule.GetAttr("query");
                    dynamic req = new PyDict();
                    req["image_path"] = new PyString(imagePath);
                    req["question"] = new PyString(question);
                    req["max_new_tokens"] = new PyInt(maxResponseTokens);
                    return queryFunc(req);
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    readonly PyModule pythonModule;
    readonly string pythonCode =
        """"
        import sys, json, torch, traceback

        from PIL import Image
        from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor, BitsAndBytesConfig
        from qwen_vl_utils import process_vision_info

        device = None
        model = None
        processor = None

        def load_model(path):
            global device, model, processor

            quantization_config = BitsAndBytesConfig(
                load_in_4bit=True,
                bnb_4bit_compute_dtype=torch.bfloat16,
                bnb_4bit_use_double_quant=True,
                bnb_4bit_quant_type="nf4"
            )

            device = torch.device("cuda")
            model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
                path,
                dtype="auto",
                quantization_config=quantization_config,
                device_map="auto",
                attn_implementation="sdpa"
            )
            processor = AutoProcessor.from_pretrained(
                path,
                min_pixels=256 * 28 * 28,
                max_pixels=512 * 28 * 28
            )

        def query(req):
            path = req.get("image_path")
            question = req.get("question")
            max_tokens = req.get("max_new_tokens")

            image = Image.open(path).convert("RGB")
            messages = [
                {
                    "role": "user",
                    "content": [
                        {"type": "image", "image": image},
                        {"type": "text", "text": question},
                    ],
                }
            ]
            text = processor.apply_chat_template(
                messages, tokenize=False, add_generation_prompt=True
            )

            image_inputs, video_inputs = process_vision_info(messages)
            inputs = processor(
                text=[text],
                images=image_inputs,
                videos=video_inputs,
                padding=True,
                return_tensors="pt",
            )
            inputs = inputs.to(device)

            with torch.no_grad():
                generated_ids = model.generate(**inputs, max_new_tokens=max_tokens)
                generated_ids_trimmed = [
                    out_ids[len(in_ids) :] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
                ]
                res = processor.batch_decode(
                    generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
                )

            del inputs, generated_ids
            torch.cuda.empty_cache()

            return res[0].strip()
        """";

    public QwenVisionModel()
    {
        //安装依赖
        const string ModelId = "Qwen/Qwen2.5-VL-3B-Instruct";
        string modelPath = AlifeModel.EnsureModelExisting(ModelId);//下载模型
        AlifePlatform.Command("python", "-m pip install torch torchvision Pillow transformers qwen-vl-utils bitsandbytes accelerate sentencepiece tiktoken");

        //加载功能
        using (Py.GIL())
        {
            pythonModule = Py.CreateScope(nameof(QwenVisionModel));
            pythonModule.Exec(pythonCode);
            pythonModule.InvokeMethod("load_model", new PyString(modelPath));
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
