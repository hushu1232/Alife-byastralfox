import sys, json, torch, traceback, io

# 强制全局使用 UTF-8 编码，防止 Windows GBK 干扰
sys.stdin = io.TextIOWrapper(sys.stdin.buffer, encoding='utf-8', errors='replace')
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')
from PIL import Image
from transformers import AutoModel, AutoTokenizer
import torchvision.transforms as T
from torchvision.transforms.functional import InterpolationMode

# 核心设备选择逻辑：强制 CUDA
if not torch.cuda.is_available():
    print(json.dumps({"status": "error", "message": "CUDA NOT FOUND. This bridge requires NVIDIA GPU."}), flush=True)
    sys.exit(1)

device = torch.device("cuda")
print(f"USING DEVICE: CUDA ({torch.cuda.get_device_name(0)})", file=sys.stderr)

"""
Vision Bridge - InternVL2.5-1B 稳定版 (CUDA)
"""

def load_model(path):
    # 猴子补丁：避开 InternVL 在某些环境下的 meta tensor 报错
    orig_linspace = torch.linspace
    torch.linspace = lambda *args, **kwargs: orig_linspace(*args, **{**kwargs, "device": "cpu"}) if "device" not in kwargs else orig_linspace(*args, **kwargs)
    
    try:
        tokenizer = AutoTokenizer.from_pretrained(path, trust_remote_code=True, fix_mistral_regex=True)
        
        # 兼容性处理：DirectML 环境下 float16 比 bfloat16 更通用
        compute_dtype = torch.float16
        
        # 加载模型到指定设备 (NVIDIA/AMD/Intel/CPU)
        model = AutoModel.from_pretrained(
            path, 
            dtype=compute_dtype, 
            trust_remote_code=True
        ).to(device).eval()
        
        return model, tokenizer
    finally:
        torch.linspace = orig_linspace

def query(model, tokenizer, req):
    path = req.get("image_path")
    if not path: return {"status": "error", "message": "image_path is required"}
    
    transform = T.Compose([
        T.Lambda(lambda img: img.convert("RGB")),
        T.Resize((448, 448), interpolation=InterpolationMode.BICUBIC),
        T.ToTensor(),
        T.Normalize(mean=(0.485, 0.456, 0.406), std=(0.229, 0.224, 0.225)),
    ])
    
    image = Image.open(path).convert("RGB")
    # 预处理后的数据也推送到 DirectML/CUDA 设备
    pixel_values = transform(image).unsqueeze(0).to(device).to(dtype=next(model.parameters()).dtype)
    
    question = req.get("question", "请详细描述这张图片。")
    max_tokens = req.get("max_new_tokens", 512)
    
    with torch.no_grad():
        # 恢复默认高性能推理模式
        res = model.chat(tokenizer, pixel_values, f"<image>\n{question}", {
            "max_new_tokens": max_tokens, 
            "do_sample": False
        })
    return {"status": "ok", "result": res.strip()}

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--model_path", required=True)
    args = parser.parse_args()

    try:
        model, tokenizer = load_model(args.model_path)
        print("READY", flush=True)
    except Exception:
        print(json.dumps({"status": "error", "message": traceback.format_exc()}), flush=True)
        sys.exit(1)

    for line in sys.stdin:
        if not (line := line.strip()): continue
        try:
            req = json.loads(line)
            response = query(model, tokenizer, req)
        except Exception:
            response = {"status": "error", "message": traceback.format_exc()}
        print(json.dumps(response, ensure_ascii=False), flush=True)
