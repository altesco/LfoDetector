from ultralytics import YOLO
from huggingface_hub import hf_hub_download
from fastapi import FastAPI
from fastapi.responses import Response
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.post("/convert_model")
async def convert_model(file_path: str):
    model = YOLO(file_path)
    onnx_path = model.export(format='onnx')
    return PlainTextResponse(onnx_path)

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=5050)