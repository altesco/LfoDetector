# LfoDetector[cite: 1]

Приложение на базе фреймворка Avalonia UI, предназначенное для автоматического обнаружения беспилотных летательных аппаратов (БПЛА) на видеоизображениях с помощью нейросетевых моделей YOLO[cite: 1, 2, 3].

## Микросервис конвертации моделей

Для запуска микросервиса, отвечающего за конвертацию весов моделей из формата `.pt` в формат `.onnx`[cite: 2, 3], требуется установить следующие Python-зависимости:

```text
annotated-doc==0.0.4
annotated-types==0.7.0
anyio==4.14.1
certifi==2026.6.17
charset-normalizer==3.4.7
click==8.4.2
colorama==0.4.6
contourpy==1.3.3
cycler==0.12.1
fastapi==0.138.2
filelock==3.29.0
flatbuffers==25.12.19
fonttools==4.63.0
fsspec==2026.4.0
h11==0.16.0
hf-xet==1.5.1
httpcore==1.0.9
httpx==0.28.1
huggingface_hub==1.21.0
idna==3.18
Jinja2==3.1.6
kiwisolver==1.5.0
markdown-it-py==4.2.0
MarkupSafe==3.0.3
matplotlib==3.11.0
mdurl==0.1.2
ml_dtypes==0.5.4
mpmath==1.3.0
networkx==3.6.1
numpy==2.4.4
nvidia-ml-py==13.610.43
onnx==1.22.0
onnxruntime==1.27.0
onnxslim==0.1.94
opencv-python==4.13.0.92
packaging==26.2
pillow==12.2.0
polars==1.42.0
polars-runtime-32==1.42.0
protobuf==7.35.1
psutil==7.2.2
pydantic==2.13.4
pydantic_core==2.46.4
Pygments==2.20.0
pyparsing==3.3.2
python-dateutil==2.9.0.post0
PyYAML==6.0.3
requests==2.34.2
rich==15.0.0
setuptools==70.2.0
shellingham==1.5.4
six==1.17.0
starlette==1.3.1
sympy==1.14.0
torch==2.12.1+cpu
torchvision==0.27.1+cpu
tqdm==4.68.3
typer==0.25.1
typing-inspection==0.4.2
typing_extensions==4.15.0
ultralytics==8.4.82
ultralytics-thop==2.0.20
urllib3==2.7.0
uvicorn==0.49.0