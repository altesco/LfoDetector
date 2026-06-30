from ultralytics import YOLO
from huggingface_hub import hf_hub_download

model_path = hf_hub_download(
    repo_id="jadenvr/YOLOv12s-VisDrone",
    filename="best.pt",
    local_dir="./LoadedModels"
)

# Load a model
model = YOLO(model_path)

# Export the model to ONNX format
model.export(format='onnx')